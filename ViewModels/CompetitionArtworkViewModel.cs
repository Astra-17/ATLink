using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using ATLink.Core;
using ATLink.Services;

namespace ATLink.ViewModels;

public sealed class CompetitionArtworkViewModel:FootballNotify
{
    readonly CompetitionProjectContext context;readonly int? competition;readonly Action? applied;
    CompetitionArtwork? original,generated;
    byte[]? logo,title;
    CancellationTokenSource? cancellation;
    public BitmapSource? LogoPreview {get;private set;}
    public BitmapSource? TitlePreview {get;private set;}
    public BitmapSource? TinyPreview {get;private set;}
    public bool Supported=>context.Game=="FC27";
    public bool Existing=>competition.HasValue;
    public bool Busy {get;private set;}
    public bool CanEdit=>Supported&&!Busy;
    public int Progress {get;private set;}
    public string Error {get;private set;}="";
    public Func<string,bool>? ConfirmShared {get;set;}
    string Stamp=>logo is null&&title is null?"":generated?.Fingerprint??Convert.ToHexString(SHA256.HashData([..logo??[],..title??[]]));
    public bool HasPending=>Busy||Stamp!=(original?.Fingerprint??"");
    public string Summary=>logo is null&&title is null?"No custom competition artwork.":generated is null?"Add both required PNG images to complete the artwork.":"Competition artwork: logo + name image · five formats · dark and light.";
    public CompetitionArtworkViewModel(CompetitionProjectContext context,int? competition=null,Action? applied=null)
    {
        this.context=context;this.competition=competition;this.applied=applied;
        if(competition.HasValue)context.Artwork.TryGetValue(competition.Value,out original);
        Restore();
    }
    public async Task ImportAsync(bool isLogo,byte[] png)
    {
        if(!CanEdit)return;
        Busy=true;Progress=0;Error="";cancellation=new();Changed();
        try
        {
            var candidate=await Task.Run(()=>CompetitionArtworkImages.Decode(png,isLogo?1024:512,isLogo?1024:128),cancellation.Token);
            var nextLogo=isLogo?png:logo;var nextTitle=isLogo?title:png;
            CompetitionArtwork? result=null;
            if(nextLogo is not null&&nextTitle is not null)
                result=await CompetitionArtworkImages.GenerateAsync(nextLogo,nextTitle,new Progress<int>(n=>{Progress=n;Notify(nameof(Progress));}),cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(isLogo){logo=png.ToArray();LogoPreview=candidate;}else{title=png.ToArray();TitlePreview=candidate;}
            generated=result;TinyPreview=result is null?null:CompetitionArtworkImages.TinyPreview(result);Progress=100;
        }
        catch(Exception ex){Error=ex is OperationCanceledException?"Image processing cancelled. Previous artwork kept.":ex.Message;}
        finally{Busy=false;cancellation.Dispose();cancellation=null;Changed();}
    }
    public void CancelPending()=>cancellation?.Cancel();
    public void ReportError(string message){Error=message;Changed();}
    public void Remove(){if(Busy)return;logo=title=null;generated=null;LogoPreview=TitlePreview=TinyPreview=null;Error="";Changed();}
    public void Restore()
    {
        if(Busy)return;generated=original;logo=original?.LogoPng;title=original?.TitlePng;
        LogoPreview=logo is null?null:CompetitionArtworkImages.Decode(logo,1024,1024);
        TitlePreview=title is null?null:CompetitionArtworkImages.Decode(title,512,128);
        TinyPreview=original is null?null:CompetitionArtworkImages.TinyPreview(original);Error="";Changed();
    }
    public CompetitionArtwork? Snapshot()
    {
        if(Busy)throw new InvalidDataException("Wait for the competition textures to finish generating.");
        if((logo is null)!=(title is null)||logo is not null&&generated is null)throw new InvalidDataException("Competition artwork requires both the 1024 × 1024 logo and the 512 × 128 name image.");
        return generated;
    }
    public void Apply()
    {
        try
        {
            if(!competition.HasValue)throw new InvalidDataException("Create the competition to apply its artwork.");
            var value=Snapshot();var shared=context.ArtworkSharing(competition.Value);
            if(shared.Count>0&&ConfirmShared?.Invoke("This graphic set is also used by "+string.Join(", ",shared.Select(o=>o.DisplayName))+". Change the appearance for all of these competitions?")!=true)return;
            context.SetArtwork(competition.Value,value,shared.Count>0);original=value;Error="";Changed();applied?.Invoke();
        }
        catch(Exception ex){Error=ex.Message;Changed();}
    }
    void Changed(){foreach(string key in new[]{nameof(LogoPreview),nameof(TitlePreview),nameof(TinyPreview),nameof(Busy),nameof(CanEdit),nameof(Progress),nameof(Error),nameof(HasPending),nameof(Summary)})Notify(key);}
}
