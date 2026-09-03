using System.Runtime.InteropServices;
using AirControl.Core;
using NAudio.Wave.Asio;

namespace AirControl.Audio;

/// <summary>
/// Implementação real de <see cref="IAsioSampleRateController"/> usando
/// <c>NAudio.Wave.Asio.AsioDriver</c> — a reimplementação do host ASIO da própria NAudio (MIT,
/// sem depender do SDK da Steinberg). Abre o driver apenas pelo tempo de uma chamada
/// (<c>Init</c>/<c>GetSampleRate</c>/<c>CanSampleRate</c>/<c>SetSampleRate</c>) e libera em
/// seguida — nunca mantém uma sessão ASIO aberta, para não bloquear o driver para outros
/// aplicativos que o usem via ASIO enquanto o AirControl continua com sua própria captura WASAPI
/// compartilhada (research.md §6, revisado).
/// </summary>
/// <remarks>
/// Teste ao vivo contra o AIR 192|4 mostrou que <c>ASIOSetSampleRate</c> confirma a mudança
/// dentro da mesma sessão do driver, mas o valor não necessariamente persiste depois que a
/// sessão é liberada (o WASAPI do Windows pode continuar reportando um sample rate diferente
/// depois). <see cref="TrySetSampleRate"/> só pode garantir o que confirma dentro da própria
/// sessão — não há como, pela API pública do ASIO, garantir persistência entre sessões.
/// </remarks>
public class AsioSampleRateController : IAsioSampleRateController
{
    private const string AirDriverNameFragment = "AIR 192";

    private static readonly int[] CandidateSampleRates = { 44100, 48000, 88200, 96000, 176400, 192000 };

    public int? GetCurrentSampleRate()
    {
        return WithDriver<int?>(driver =>
        {
            var rate = driver.GetSampleRate();
            return rate > 0 && !double.IsNaN(rate) ? (int)rate : null;
        });
    }

    public IReadOnlyList<int> GetSupportedSampleRates()
    {
        // Lista fixa de candidatos (ver remarks de IAsioSampleRateController.GetSupportedSampleRates)
        // — apenas confirma que o driver está disponível antes de oferecer opções na UI.
        return WithDriver(_ => (IReadOnlyList<int>)CandidateSampleRates.ToList()) ?? Array.Empty<int>();
    }

    public bool TrySetSampleRate(int sampleRate, out string? error)
    {
        var driverName = FindAirDriverName();
        if (driverName is null)
        {
            error = "Driver ASIO do M-Audio não encontrado.";
            return false;
        }

        AsioDriver? driver = null;
        try
        {
            driver = OpenDriver(driverName);
            driver.SetSampleRate(sampleRate);

            // Confirma lendo de volta em vez de assumir sucesso pela ausência de exceção: um
            // teste ao vivo contra o AIR 192|4 mostrou que a mudança pode não se sustentar depois
            // que a sessão ASIO é liberada (research.md §6, revisado) — reportar aqui, dentro da
            // mesma sessão, é o máximo que dá para confirmar de forma confiável.
            var confirmed = driver.GetSampleRate();
            if (Math.Abs(confirmed - sampleRate) > 0.5)
            {
                error = $"O driver não confirmou a mudança: pedido {sampleRate}Hz, mas reporta {confirmed}Hz.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (IsAsioFailure(ex))
        {
            error = $"Falha ao aplicar o sample rate no driver ASIO: {ex.Message}";
            return false;
        }
        finally
        {
            ReleaseSafely(driver);
        }
    }

    private static TResult? WithDriver<TResult>(Func<AsioDriver, TResult> action)
    {
        var driverName = FindAirDriverName();
        if (driverName is null)
        {
            return default;
        }

        AsioDriver? driver = null;
        try
        {
            driver = OpenDriver(driverName);
            return action(driver);
        }
        catch (Exception ex) when (IsAsioFailure(ex))
        {
            return default;
        }
        finally
        {
            ReleaseSafely(driver);
        }
    }

    private static AsioDriver OpenDriver(string driverName)
    {
        var driver = AsioDriver.GetAsioDriverByName(driverName);
        driver.Init(IntPtr.Zero);
        return driver;
    }

    private static void ReleaseSafely(AsioDriver? driver)
    {
        try
        {
            driver?.ReleaseComAsioDriver();
        }
        catch (Exception ex) when (IsAsioFailure(ex))
        {
            // Best-effort: o driver já pode ter sido desconectado/removido entre a abertura e o release.
        }
    }

    private static string? FindAirDriverName() =>
        AsioDriver.GetAsioDriverNames().FirstOrDefault(name => name.Contains(AirDriverNameFragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Inclui <c>NAudio.Wave.Asio.AsioException</c> por nome de tipo (não por referência direta —
    /// a classe é <c>internal</c> ao assembly da NAudio) porque <c>AsioDriver.SetSampleRate</c> a
    /// lança para rates que o driver realmente rejeita; deixá-la escapar sem tratamento aqui
    /// interromperia <see cref="TrySetSampleRate"/> antes do <c>finally</c> restaurar o engine no
    /// chamador, travando a captura (mesma classe de bug corrigida em
    /// DriverSettingsViewModel.OnSelectedSampleRateChanged).
    /// </summary>
    private static bool IsAsioFailure(Exception ex) =>
        ex is COMException or InvalidOperationException or ExternalException
        || ex.GetType().FullName == "NAudio.Wave.Asio.AsioException";
}
