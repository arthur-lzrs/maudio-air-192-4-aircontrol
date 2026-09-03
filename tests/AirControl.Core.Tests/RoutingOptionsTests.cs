using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

/// <summary>
/// Regressão de S1 (research.md §1): com <c>ActiveInputChannelCount == 0</c> o seletor de modo de
/// roteamento filtrava TODOS os modos e ficava vazio, sem mensagem — FR-002/FR-003 exigem um
/// estado explícito de "não determinável" com mensagem acionável, nunca lista vazia silenciosa.
/// </summary>
public class RoutingOptionsTests
{
    [Fact]
    public void Resolve_WithTwoChannels_OffersAllModes()
    {
        var state = RoutingOptionsState.Resolve(2);

        Assert.True(state.IsDeterminable);
        Assert.Null(state.Message);
        Assert.Equal(Enum.GetValues<RoutingMode>(), state.AvailableModes);
    }

    [Fact]
    public void Resolve_WithOneChannel_OffersOnlyInput1Mono()
    {
        var state = RoutingOptionsState.Resolve(1);

        Assert.True(state.IsDeterminable);
        Assert.Null(state.Message);
        Assert.Equal(new[] { RoutingMode.Input1Mono }, state.AvailableModes);
    }

    [Fact]
    public void Resolve_WithZeroChannels_IsNotDeterminableAndCarriesActionableMessage()
    {
        var state = RoutingOptionsState.Resolve(0);

        Assert.False(state.IsDeterminable);
        Assert.Empty(state.AvailableModes);
        Assert.False(string.IsNullOrWhiteSpace(state.Message));
    }

    /// <summary>FR-003: uma lista vazia SÓ pode existir acompanhada de mensagem (estado não determinável).</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void Resolve_NeverProducesSilentEmptyList(int channelCount)
    {
        var state = RoutingOptionsState.Resolve(channelCount);

        if (state.AvailableModes.Count == 0)
        {
            Assert.False(state.IsDeterminable);
            Assert.False(string.IsNullOrWhiteSpace(state.Message));
        }
        else
        {
            Assert.True(state.IsDeterminable);
            Assert.Null(state.Message);
        }
    }

    /// <summary>FR-004: um dispositivo válido voltando repopula as opções sem nenhum estado residual.</summary>
    [Fact]
    public void Resolve_AfterIndeterminable_RepopulatesWhenChannelsReturn()
    {
        var indeterminable = RoutingOptionsState.Resolve(0);
        var recovered = RoutingOptionsState.Resolve(2);

        Assert.False(indeterminable.IsDeterminable);
        Assert.True(recovered.IsDeterminable);
        Assert.Equal(Enum.GetValues<RoutingMode>(), recovered.AvailableModes);
    }

    /// <summary>Cada modo oferecido tem que ser realmente suportado pela contagem de canais (FR-005).</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Resolve_OnlyOffersModesSupportedByTheChannelCount(int channelCount)
    {
        var state = RoutingOptionsState.Resolve(channelCount);

        Assert.All(state.AvailableModes, mode => Assert.True(RoutingModeApplier.IsSupported(mode, channelCount)));
    }
}
