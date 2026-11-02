namespace Stx.Look;

public enum ViewState { Loading, Error, Empty, Disabled }

public enum LabelKind { Title, Label, Sub }

public readonly record struct StateAppearance(LedColor Color, LedState Led, bool Dimmed, bool OffersAction);

/// <summary>How each non-content state reads on the hardware: an indicator LED and a line on the display.</summary>
public static class StateLook
{
    public static StateAppearance For(ViewState state) => state switch
    {
        ViewState.Loading => new(LedColor.Amber, LedState.Busy, Dimmed: false, OffersAction: false),
        ViewState.Error => new(LedColor.Red, LedState.On, Dimmed: false, OffersAction: true),
        ViewState.Disabled => new(LedColor.Green, LedState.Off, Dimmed: true, OffersAction: false),
        _ => new(LedColor.Green, LedState.Off, Dimmed: true, OffersAction: false),
    };
}

public readonly record struct LabelStyle(string InkToken, double Size, int Tracking, bool Engraved);

/// <summary>The panel type sizes from the approved mockup.</summary>
public static class LabelStyles
{
    public static LabelStyle For(LabelKind kind) => kind switch
    {
        LabelKind.Title => new("Ink", 11.5, 240, Engraved: true),
        LabelKind.Sub => new("InkDim", 9.5, 300, Engraved: false),
        _ => new("InkDim", 10.5, 160, Engraved: false),
    };
}
