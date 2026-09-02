using System.Windows;
using System.Windows.Controls;
using AirControl.Core;

namespace AirControl.App.Views;

public partial class MeterControl : UserControl
{
    public static readonly DependencyProperty PeakDbProperty = DependencyProperty.Register(
        nameof(PeakDb), typeof(double), typeof(MeterControl), new PropertyMetadata(LevelMetering.SilenceFloorDb));

    public static readonly DependencyProperty RmsDbProperty = DependencyProperty.Register(
        nameof(RmsDb), typeof(double), typeof(MeterControl), new PropertyMetadata(LevelMetering.SilenceFloorDb));

    public static readonly DependencyProperty IsClippingProperty = DependencyProperty.Register(
        nameof(IsClipping), typeof(bool), typeof(MeterControl), new PropertyMetadata(false));

    public static readonly DependencyProperty IsDeviceConnectedProperty = DependencyProperty.Register(
        nameof(IsDeviceConnected), typeof(bool), typeof(MeterControl), new PropertyMetadata(false));

    public static readonly DependencyProperty ChannelLabelProperty = DependencyProperty.Register(
        nameof(ChannelLabel), typeof(string), typeof(MeterControl), new PropertyMetadata(string.Empty));

    public MeterControl()
    {
        InitializeComponent();
    }

    public double PeakDb
    {
        get => (double)GetValue(PeakDbProperty);
        set => SetValue(PeakDbProperty, value);
    }

    public double RmsDb
    {
        get => (double)GetValue(RmsDbProperty);
        set => SetValue(RmsDbProperty, value);
    }

    public bool IsClipping
    {
        get => (bool)GetValue(IsClippingProperty);
        set => SetValue(IsClippingProperty, value);
    }

    public bool IsDeviceConnected
    {
        get => (bool)GetValue(IsDeviceConnectedProperty);
        set => SetValue(IsDeviceConnectedProperty, value);
    }

    public string ChannelLabel
    {
        get => (string)GetValue(ChannelLabelProperty);
        set => SetValue(ChannelLabelProperty, value);
    }
}
