using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShadUI.Demo.ViewModels.Examples.Typography;

public sealed partial class LabelViewModel : ViewModelBase
{
    public LabelViewModel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Typography", "LabelContent.axaml");
        H1Code = path.ExtractByLineRange(18, 20).CleanIndentation();
        H2Code = path.ExtractByLineRange(23, 25).CleanIndentation();
        H3Code = path.ExtractByLineRange(28, 30).CleanIndentation();
        H4Code = path.ExtractByLineRange(33, 35).CleanIndentation();
        PCode = path.ExtractByLineRange(38, 40).CleanIndentation();
        LeadCode = path.ExtractByLineRange(43, 45).CleanIndentation();
        LargeCode = path.ExtractByLineRange(48, 50).CleanIndentation();
        SmallCode = path.ExtractByLineRange(53, 55).CleanIndentation();
        CaptionCode = path.ExtractByLineRange(58, 60).CleanIndentation();
        MutedCode = path.ExtractByLineRange(63, 65).CleanIndentation();
        ErrorCode = path.ExtractByLineRange(68, 70).CleanIndentation();
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