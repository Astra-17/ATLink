using System.Text.Json;
namespace ATLink.Core;
public sealed record RatingInput(int Age,string Position,double MarketValue,double? ClubMean=null,double? LeagueMean=null,double? WeightedTrophies=null,int? BestMarketRank=null,int Seed=1);
public sealed record RatingResult(int RawOverall,int Overall,int Potential,int Reputation,IReadOnlyDictionary<string,int> Attributes,double AbilityScore);
public static class PlayerRatings
{
    private static readonly Dictionary<string,Dictionary<string,double>> Weights=Load();
    public static IReadOnlyList<string> Positions=>Weights.Keys.ToArray();
    private static Dictionary<string,Dictionary<string,double>> Load()
    {
        using var stream=typeof(PlayerRatings).Assembly.GetManifestResourceStream("ATLink.Core.Resources.RatingWeights.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,double>>>(stream)!;
    }
    private static double Unit(double value)=>Math.Clamp(value,0,1);
    private static int Round(double value)=>(int)Math.Floor(value+0.5);
    private static double Market(double value)=>Unit(Math.Log(Math.Max(50000,value)/50000)/Math.Log(180000000.0/50000));
    private static double Relative(double value,double mean)=>.5+Math.Atan(Math.Log(value/mean)/1.15)/Math.PI;
    private static double Mean(params (double? Value,double Weight)[] values){var known=values.Where(v=>v.Value.HasValue).ToArray();return known.Sum(v=>v.Value!.Value*v.Weight)/known.Sum(v=>v.Weight);}
    public static RatingResult Calculate(RatingInput input)
    {
        if(input.Age is <15 or >80||!Weights.ContainsKey(input.Position)||!double.IsFinite(input.MarketValue)||input.MarketValue<=0||new[]{input.ClubMean,input.LeagueMean}.Any(v=>v.HasValue&&(!double.IsFinite(v.Value)||v.Value<=0))||input.WeightedTrophies is double t&&(!double.IsFinite(t)||t<0)||input.BestMarketRank is <=0)throw new InvalidDataException("Âge, poste, valeur marchande et contexte valides requis.");
        double adjusted=input.MarketValue/Math.Exp(input.Age<=27?.05*(27-input.Age):-.1*(input.Age-27));
        double context=Math.Clamp((input.LeagueMean.HasValue?Math.Pow(input.LeagueMean.Value/5000000,.16):1)*(input.ClubMean.HasValue?Math.Pow(input.ClubMean.Value/4000000,.08):1),.65,1.55);
        double market=Market(adjusted*context);
        double? trophy=input.WeightedTrophies.HasValue?1-Math.Exp(-input.WeightedTrophies.Value/8):null;
        double? league=input.LeagueMean.HasValue?Market(input.LeagueMean.Value):null;
        double ability=Mean((market,.48),(input.ClubMean.HasValue?Relative(adjusted,input.ClubMean.Value):null,.04),(input.LeagueMean.HasValue?Relative(adjusted,input.LeagueMean.Value):null,.02),(trophy,.06),(league,.4));
        double curve=Unit(ability+.08*64*Math.Pow(ability,3)*Math.Pow(1-ability,3));
        double experience=input.Position=="GK"&&input.Age>31?.135*(1-Math.Exp(-(input.Age-31)/3.0))*Math.Min(1,(1-curve)/.5):0;
        int raw=Round(45+Unit(curve+(input.Position=="GK"?-.02:0)+experience)*51);
        double reputationScore=Mean((input.BestMarketRank.HasValue?1/(1+Math.Log(1+input.BestMarketRank.Value)/4):market,.4),(trophy,.25),(input.ClubMean.HasValue?Market(input.ClubMean.Value):null,.18),(league,.17));
        int reputation=Round(1+reputationScore*4);
        int boost=reputation switch{3 when raw>50=>1,4 when raw>=67=>2,4 when raw>=33=>1,5 when raw>=75=>3,5 when raw>=50=>2,5 when raw>=25=>1,_=>0};
        int overall=Math.Min(99,raw+boost);
        return new(raw,overall,Potential(overall,input.Age,input.MarketValue,input.Position),reputation,Generate(input.Position,raw,input.Seed),ability);
    }
    public static int Potential(int overall,int age,double market,string position)
    {
        if(overall is <0 or >99||age is <15 or >80||!double.IsFinite(market)||market<=0||!Weights.ContainsKey(position))throw new InvalidDataException("Paramètres de potentiel invalides.");
        overall=Math.Max(45,overall);if(age>=28)return overall;
        bool late=new[]{"GK","CB","RCB","LCB","CDM","RDM","LDM","CM","RCM","LCM"}.Contains(position);
        double factor=Unit(((45+Market(market)*50)-overall+8)/18);
        double floor=age<=18?.28:age<=21?.22:age<=23?.14:age<=25?.08:0;
        int ceiling=age switch{<=16=>20,17=>18,18=>16,19=>14,20=>12,21=>10,22=>8,23=>6,24=>5,25=>3,26=>2,27=>1,_=>0};if(age>=26&&late)ceiling++;
        double positionFactor=position=="GK"?(age<=21?.9:1.18):late?(age>=22?1.12:1.02):new[]{"RW","LW","RF","LF","RS","LS","ST"}.Contains(position)?(age>=24?.92:1.04):new[]{"CAM","RAM","LAM","CF"}.Contains(position)?(age>=24?.96:1.02):1;
        double score=age<26||factor>=(age==27?.74:.58)?Unit(floor+factor*(1-floor)):0;
        int minimum=age<=18?4:age<=20?3:age<=23?2:age==24?(late?2:1):0;
        int growth=Math.Max(minimum,Round(ceiling*score*positionFactor*(1-Math.Clamp((overall-84)/20.0,0,.45))));
        return Math.Min(99,overall+growth);
    }
    public static int RawOverall(string position,IReadOnlyDictionary<string,int> attributes)=>Math.Min(99,Round(Weights[position].Sum(kv=>attributes[kv.Key]*kv.Value)));
    public static IReadOnlyDictionary<string,int> Generate(string position,int target,int seed)
    {
        if(!Weights.ContainsKey(position)||target is <1 or >99)throw new InvalidDataException("Poste/note invalide.");
        var random=new Random(seed);var main=Weights[position];
        var universal=new HashSet<string>{"acceleration","aggression","agility","balance","jumping","reactions","sprintspeed","stamina","strength"};
        var attributes=Weights.Values.SelectMany(w=>w.Keys).Distinct().ToDictionary(a=>a,a=>
        {
            double mean=main.ContainsKey(a)?target:universal.Contains(a)?.7*target:.2*target;
            double deviation=main.ContainsKey(a)?5:universal.Contains(a)?10:7.5;
            double normal=Math.Sqrt(-2*Math.Log(1-random.NextDouble()))*Math.Cos(2*Math.PI*random.NextDouble());
            return Math.Clamp(Round(mean+deviation*normal),1,99);
        });
        // Bounded adjustment preserves legal attributes even at the extremes.
        var keys=main.Keys.ToArray();
        for(int i=0;i<100000;i++)
        {
            int current=RawOverall(position,attributes);if(current==target)return attributes;
            string key=keys[i%keys.Length];attributes[key]=Math.Clamp(attributes[key]+Math.Sign(target-current),1,99);
        }
        throw new InvalidDataException("Impossible d'atteindre la note demandée.");
    }
}
