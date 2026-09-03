using System.Runtime.InteropServices;

namespace AirControl.Audio;

/// <summary>
/// Escreve o "Formato Padrão" de um endpoint de gravação via <c>IPolicyConfig::SetDeviceFormat</c>
/// — a interface COM interna (não documentada publicamente pela Microsoft, mas estável desde o
/// Windows 7 e usada por diversas ferramentas conhecidas de troca de dispositivo/formato padrão)
/// que o próprio Painel de Som do Windows usa internamente. A primeira implementação desta
/// classe usava <c>IMMDevice::OpenPropertyStore</c> + <c>IPropertyStore::SetValue</c> em
/// <c>PKEY_AudioEngine_DeviceFormat</c> — tecnicamente documentada, mas comprovadamente não
/// confiável neste driver: a chamada COM retorna sucesso sem lançar exceção, porém o valor lido
/// de volta nunca muda (confirmado por teste ao vivo contra o AIR 192|4; a própria Microsoft
/// reconhece o mesmo problema em fóruns oficiais). <c>IPolicyConfig::SetDeviceFormat</c> é o
/// mecanismo que efetivamente funciona — confirmado ao vivo aplicando e persistindo 48kHz/32-bit
/// depois que o Windows havia derivado sozinho para 44.1kHz, sem qualquer intervenção manual.
/// A peça que faltava era o sub-formato: o AIR 192|4 usa PCM inteiro mesmo em 32-bit, não IEEE
/// float (a convenção mais comum) — pedir o sub-formato errado, mesmo pro sample rate/bit depth
/// já ativos, é rejeitado com AUDCLNT_E_UNSUPPORTED_FORMAT. Isolada em AirControl.Audio para não
/// vazar interop para AirControl.Core/App (Constitution I).
/// </summary>
internal static class DeviceFormatPropertyStore
{
    private static readonly Guid ClsidPolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    public static void WriteDefaultFormat(string deviceId, int sampleRate, int bitDepth)
    {
        var policyConfigType = Type.GetTypeFromCLSID(ClsidPolicyConfigClient, throwOnError: true)!;
        var policyConfig = (IPolicyConfig)Activator.CreateInstance(policyConfigType)!;
        try
        {
            var waveFormat = BuildWaveFormatExtensible(sampleRate, bitDepth);
            var size = Marshal.SizeOf<WaveFormatExtensible>();
            var formatPtr = Marshal.AllocCoTaskMem(size);
            try
            {
                Marshal.StructureToPtr(waveFormat, formatPtr, fDeleteOld: false);
                ThrowIfFailed(policyConfig.SetDeviceFormat(deviceId, formatPtr, formatPtr));
            }
            finally
            {
                Marshal.FreeCoTaskMem(formatPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policyConfig);
        }
    }

    private static readonly Guid KsDataFormatSubtypePcm = new("00000001-0000-0010-8000-00AA00389B71");

    /// <summary>
    /// O AIR 192|4 rejeita (AUDCLNT_E_UNSUPPORTED_FORMAT) um WAVEFORMATEX simples via
    /// <c>IPolicyConfig::SetDeviceFormat</c> — confirmado por teste ao vivo. Precisa da forma
    /// WAVEFORMATEXTENSIBLE (com máscara de canais e sub-formato explícitos), a mesma exigência
    /// já conhecida deste driver em <see cref="AudioEngine.CreateAndStartCapture"/>.
    /// </summary>
    private static WaveFormatExtensible BuildWaveFormatExtensible(int sampleRate, int bitDepth)
    {
        const ushort waveFormatExtensible = 0xFFFE;
        const int channels = 2;

        // Interfaces profissionais (incluindo o AIR 192|4) tipicamente não suportam 24-bit
        // "empacotado" em 3 bytes por amostra — apenas 24-bit dentro de um container de 32 bits.
        var containerBits = bitDepth == 24 ? 32 : bitDepth;
        var blockAlign = (ushort)(channels * (containerBits / 8));

        return new WaveFormatExtensible
        {
            FormatTag = waveFormatExtensible,
            Channels = channels,
            SamplesPerSec = (uint)sampleRate,
            AvgBytesPerSec = (uint)(sampleRate * blockAlign),
            BlockAlign = blockAlign,
            BitsPerSample = (ushort)containerBits,
            ExtraSize = 22,
            ValidBitsPerSample = (ushort)bitDepth,
            ChannelMask = 0x3, // SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT
            // O AIR 192|4 usa PCM inteiro mesmo em 32-bit, não IEEE float (confirmado lendo o
            // valor bruto de PKEY_AudioEngine_DeviceFormat no registro num estado 48kHz/32-bit
            // que se sabia funcionar) — assumir float para 32-bit, a convenção mais comum, é o
            // que causava AUDCLNT_E_UNSUPPORTED_FORMAT mesmo pedindo o formato já ativo.
            SubFormat = KsDataFormatSubtypePcm,
        };
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatExtensible
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int useDefault, out IntPtr format);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int useDefault, out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr shareMode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr shareMode);

        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, out IntPtr value);

        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
