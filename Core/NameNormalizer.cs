using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ATLink.Core;

public sealed class NameNormalizer
{
    public const double FuzzyThreshold = 0.88;

    private static readonly Dictionary<char,char> InvisibleMap=new()
    {
        ['\u00A0']=' ',['\u2007']=' ',['\u202F']=' ',
        ['\u200B']='\0',['\u200C']='\0',['\u200D']='\0',['\uFEFF']='\0'
    };
    private static readonly HashSet<string> TeamNoiseTokens=new(StringComparer.OrdinalIgnoreCase)
    {
        "fc","afc","cf","ac","sc","ssc","ss","bc","sv","club","football","futbol","fussball"
    };
    private static readonly HashSet<string> FreeAgentNames=new(StringComparer.Ordinal)
    {
        "sans club","sans equipe","libre","agent libre","agents libres","free agent","free agents"
    };
    private static readonly HashSet<string> RetirementNames=new(StringComparer.Ordinal)
    {
        "fin de carriere","retraite","retired","career end","end of career"
    };
    private static readonly Regex MultiSpace=new(@"\s+",RegexOptions.Compiled);
    private static readonly Regex NonAlphaNum=new(@"[^a-z0-9 ]+",RegexOptions.Compiled);

    public string Clean(string? value)
    {
        if(string.IsNullOrEmpty(value))return "";
        var decoded=WebUtility.HtmlDecode(value);
        var builder=new StringBuilder(decoded.Length);
        foreach(var c in decoded)
        {
            if(InvisibleMap.TryGetValue(c,out var mapped))
            {
                if(mapped!='\0')builder.Append(mapped);
                continue;
            }
            builder.Append(c);
        }
        return MultiSpace.Replace(builder.ToString().Normalize(NormalizationForm.FormC)," ").Trim();
    }

    public string Normalize(string? value)=>NormalizeCore(value);
    public string NormalizePersonName(string? value)=>NormalizeCore(value,true);
    public string NormalizeTeamName(string? value)=>NormalizeCore(value);
    public bool IsFreeAgentName(string? value)=>FreeAgentNames.Contains(NormalizeTeamName(value));
    public bool IsRetirementName(string? value)=>RetirementNames.Contains(NormalizeTeamName(value));

    public IReadOnlyList<string> Tokens(string? value)
    {
        var normalized=NormalizePersonName(value);
        return normalized.Length==0?[]:normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries);
    }

    public string TeamKey(string? value)
    {
        var tokens=NormalizeTeamName(value).Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>!TeamNoiseTokens.Contains(t)).ToArray();
        return string.Join(' ',tokens);
    }

    public double Similarity(string? left,string? right)
    {
        var a=NormalizePersonName(left);var b=NormalizePersonName(right);
        if(a.Length==0&&b.Length==0)return 1;
        if(a.Length==0||b.Length==0)return 0;
        if(a==b)return 1;
        var popular=b.Length>=200
            ?b.GroupBy(c=>c).Where(g=>g.Count()>b.Length/100+1).Select(g=>g.Key).ToHashSet()
            :[];
        return 2.0*CountMatchingBlocks(a,0,a.Length,b,0,b.Length,popular)/(a.Length+b.Length);
    }

    string NormalizeCore(string? value,bool personName=false)
    {
        var cleaned=Clean(value).ToLowerInvariant();
        if(cleaned.Length==0)return "";
        if(personName)cleaned=cleaned.Replace("æ","ae").Replace('ø','o').Replace('ł','l');
        cleaned=cleaned.Normalize(NormalizationForm.FormKD)
            .Replace('’','\'').Replace('‘','\'').Replace('ʼ','\'').Replace('`','\'').Replace('´','\'')
            .Replace("&"," and ").Replace('-',' ').Replace('.',' ').Replace('\'',' ');
        var builder=new StringBuilder(cleaned.Length);
        foreach(var c in cleaned)
            if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)builder.Append(c);
        return MultiSpace.Replace(NonAlphaNum.Replace(builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()," ")," ").Trim();
    }

    static int CountMatchingBlocks(string a,int aStart,int aEnd,string b,int bStart,int bEnd,HashSet<char> popular)
    {
        if(aStart>=aEnd||bStart>=bEnd)return 0;
        var (ai,bj,length)=LongestCommonSubstring(a,aStart,aEnd,b,bStart,bEnd,popular);
        if(length==0)return 0;
        return length+CountMatchingBlocks(a,aStart,ai,b,bStart,bj,popular)+CountMatchingBlocks(a,ai+length,aEnd,b,bj+length,bEnd,popular);
    }

    static (int IndexA,int IndexB,int Length) LongestCommonSubstring(string a,int aStart,int aEnd,string b,int bStart,int bEnd,HashSet<char> popular)
    {
        var bLen=bEnd-bStart;var prev=new int[bLen+1];var curr=new int[bLen+1];var best=0;var bestA=aStart;var bestB=bStart;
        for(var i=aStart;i<aEnd;i++)
        {
            Array.Clear(curr);
            for(var j=bStart;j<bEnd;j++)
            {
                if(a[i]==b[j]&&!popular.Contains(b[j]))
                {
                    var n=prev[j-bStart]+1;curr[j-bStart+1]=n;
                    if(n>best){best=n;bestA=i-n+1;bestB=j-n+1;}
                }
            }
            (prev,curr)=(curr,prev);
        }
        while(bestA>aStart&&bestB>bStart&&a[bestA-1]==b[bestB-1]){bestA--;bestB--;best++;}
        while(bestA+best<aEnd&&bestB+best<bEnd&&a[bestA+best]==b[bestB+best])best++;
        return (bestA,bestB,best);
    }
}
