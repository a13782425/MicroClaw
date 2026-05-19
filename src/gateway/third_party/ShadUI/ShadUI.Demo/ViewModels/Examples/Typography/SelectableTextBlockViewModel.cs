using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShadUI.Demo.ViewModels.Examples.Typography;

public sealed partial class SelectableTextBlockViewModel : ViewModelBase
{
    public SelectableTextBlockViewModel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Typography",
            "SelectableTextBlockContent.axaml");
        H1Code = path.ExtractByLineRange(18, 21).CleanIndentation();
        H2Code = path.ExtractByLineRange(24, 27).CleanIndentation();
        H3Code = path.ExtractByLineRange(30, 33).CleanIndentation();
        H4Code = path.ExtractByLineRange(36, 39).CleanIndentation();
        PCode = path.ExtractByLineRange(42, 45).CleanIndentation();
        LeadCode = path.ExtractByLineRange(48, 51).CleanIndentation();
        LargeCode = path.ExtractByLineRange(54, 57).CleanIndentation();
        SmallCode = path.ExtractByLineRange(60, 63).CleanIndentation();
        CaptionCode = path.ExtractByLineRange(66, 69).CleanIndentation();
        MutedCode = path.ExtractByLineRange(72, 75).CleanIndentation();
        ErrorCode = path.ExtractByLineRange(78, 81).CleanIndentation();
    }

    [ObservableProperty]
    private string _h1Code = string.Empty;

    [ObservableProperty]
    private string _h2Code = string.Empty;

    [ObservableProperty]
    private string _h3Code = string.Empty;

    [ObservableProperty]
    private string _h4Code = string.Empty;

    [ObservableProperty]
    private string _pCode = string.Empty;

    [ObservableProperty]
    private string _leadCode = string.Empty;

    [ObservableProperty]
    private string _largeCode = string.Empty;

    [ObservableProperty]
    private string _smallCode = string.Empty;

    [ObservableProperty]
    private string _captionCode = string.Empty;

    [ObservableProperty]
    private string _mutedCode = string.Empty;

    [ObservableProperty]
    private string _errorCode = string.Empty;
}