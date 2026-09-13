using ATLink.Core;
using System.Data;
using System.Text.Json;

internal static class TransferIntegrationChecks
{
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static IReadOnlyList<string> SquadJerseys(FootballCatalog catalog,string teamId)=>catalog.Rows("teamplayerlinks")
        .Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))&&FootballCatalog.Value(r,"teamid")==teamId)
        .Select(r=>FootballCatalog.Value(r,"jerseynumber")).ToArray();
    public static async Task Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Checks/Fixtures/ttlive-parity.json")));
        var players=fixture.RootElement.GetProperty("players").Deserialize<EnrichedPlayerInfo[]>(Json)!;
        var teams=fixture.RootElement.GetProperty("teams").EnumerateArray().Select(t=>new LiveTeam(t.GetProperty("teamId").GetInt32(),t.GetProperty("teamName").GetString()!,false)).ToArray();
        var names=new NameNormalizer();
        var clubs=new ClubNameResolver(teams,names,LiveClubAliases.Load(names));
        var belgianClubs=new ClubNameResolver([new LiveTeam(1,"Cercle Brugge",false),new LiveTeam(2,"R. Union St.-G.",false)],names,LiveClubAliases.Load(names));
        Require(belgianClubs.ResolveClub("Cercle Bruges").TeamId==1,"Cercle Bruges alias");
        Require(belgianClubs.ResolveClub("Union Saint-Gilloise").TeamId==2,"Union Saint-Gilloise alias");
        var abbreviationClubs=new ClubNameResolver([
            new LiveTeam(20,"Queens Park Rangers",false),new LiveTeam(21,"Olympique Lyon",false),new LiveTeam(22,"Olympique Marseille",false),
            new LiveTeam(23,"PSV Eindhoven",false),new LiveTeam(24,"Aarhus GF",false),new LiveTeam(25,"Sint-Truidense VV",false),
            new LiveTeam(26,"AZ Alkmaar",false),new LiveTeam(27,"Independiente del Valle",false),new LiveTeam(28,"Los Angeles FC",false)],names,LiveClubAliases.Load(names));
        foreach(var pair in new Dictionary<string,int>{{"QPR",20},{"OL",21},{"OM",22},{"PSV",23},{"AGF",24},{"STVV",25},{"AZ",26},{"IDV",27},{"LAFC",28}})
            Require(abbreviationClubs.ResolveClub(pair.Key).TeamId==pair.Value,$"Club abbreviation {pair.Key}");
        var fuzzyTie=new ClubNameResolver([new LiveTeam(800,"Test Cluba",false),new LiveTeam(120,"Test Clubb",false)],names);
        var fuzzyTieResult=fuzzyTie.ResolveClub("Test Clubx");
        Require(fuzzyTieResult.TeamId==120&&fuzzyTieResult.MatchMethod=="fuzzy"&&fuzzyTieResult.Confidence>0.83,"Equal fuzzy score chooses lowest team ID");
        var resolver=new TransferLiveResolver(EnrichedPlayers.FromPlayers(players),clubs,names);
        var missingLoanPlayer=resolver.Resolve([new MarketTransfer(999999,"Definitely Missing Player","Club A","PSG","",true,false,null,"Date de fin du prêt introuvable")]);
        Require(missingLoanPlayer.Unresolved.Single().Reason==UnresolvedReasons.PlayerNotFound,"Missing player reason must take priority over missing loan date");
        int count=0;
        foreach(var group in fixture.RootElement.GetProperty("groups").EnumerateArray())
        foreach(var row in group.GetProperty("cases").EnumerateArray())
        {
            string Text(string name)=>row.GetProperty(name).ValueKind==JsonValueKind.Null?"":row.GetProperty(name).ToString();
            var input=new MarketTransfer(row.GetProperty("sequence").GetInt32(),Text("player"),Text("oldClub"),Text("newClub"),"");
            var player=resolver.ResolvePlayer(input.Player,input.OldClub,out var pm,out _);
            var team=clubs.ResolveClub(input.NewClub);
            var result=resolver.ResolveOne(input);
            Require((player?.PlayerId??"")==Text("playerId"),$"Player differs: {input.Player}");
            Require((team.TeamId?.ToString()??"")==Text("teamId"),$"Team differs: {input.NewClub}");
            Require(pm==Text("playerMethod"),$"Player method differs: {input.Player}");
            Require(team.MatchMethod==Text("teamMethod"),$"Team method differs: {input.NewClub}");
            Require((result.Unresolved?.Reason??"")==Text("reason"),$"Outcome differs: {input.Sequence} {input.Player}");
            count++;
        }
        Console.WriteLine($"PASS exact TTLive parity: {count} transfers, player/team IDs, methods, unresolved reasons");
        Require(CompetitionCatalog.TransferChoices().Count==33,"33 competitions");
        Require(CompetitionCatalog.TransferChoices().Select(c=>c.TransfermarktUrl).Distinct().Count()==33,"Distinct URLs");
        Require(CompetitionCatalog.TransferChoices().All(c=>new Uri(c.TransfermarktUrl!).Host=="www.transfermarkt.com"),"All backend competition URLs use transfermarkt.com");
        using var source=new TransfermarktSource(new TransferLog(),names);
        var dateCases=new Dictionary<int,DateOnly>{{161962,new(2026,3,22)},{162032,new(2026,5,31)},{162062,new(2026,6,30)},{162069,new(2026,7,7)},{162427,new(2027,6,30)}};
        foreach(var pair in dateCases)Require(Fc26Date.Decode(pair.Key)==pair.Value&&Fc26Date.Encode(pair.Value)==pair.Key,$"FC26 date conversion {pair.Key}");
        Require(Fc26Date.Decode(161963)==new DateOnly(2026,3,23)&&Fc26Date.LoanEndChoices().Last()==new DateOnly(2035,7,1),"FC26 dates advance one day and choices reach 2035");
        var parsed=source.ParseHtml(File.ReadAllText(Path.Combine(root,"Checks/Fixtures/ttlive-sample.html")));
        Require(parsed.Count>0&&parsed.First().PlayerName=="Kevin De Bruyne","TTLive parser names");
        var combined=TransferImport.Combine([parsed,parsed]);
        Require(combined.Count==parsed.Count*2&&combined.Select(t=>t.Sequence).SequenceEqual(Enumerable.Range(1,combined.Count)),"Preserve movements and number multiple leagues");
        Require(!source.IsTransfermarktUrl("https://transfermarkt.evil.test/"),"Host allowlist");
        string loanHtml="""<div class='box'><h2><a href='/club/transfers/verein/1'>Club A</a></h2><div class='responsive-table'><table><thead><tr><th class='spieler-transfer-cell'>Arrivées</th></tr></thead><tbody><tr><td><a title='Player Loan' href='/player/profil/spieler/7'>Player Loan</a></td><td class='verein-flagge-transfer-cell'><a title='Club B' href='/club/verein/2'>Club B</a></td><td class='abloese'>Montant du prêt: 2m</td></tr><tr><td><a title='Player Loan 2' href='/player2/profil/spieler/8'>Player Loan 2</a></td><td class='verein-flagge-transfer-cell'><a title='Club C' href='/club/verein/3'>Club C</a></td><td class='abloese'>Prêt</td></tr></tbody></table></div></div>""";
        var loanParsed=source.ParseHtml(loanHtml);
        Require(loanParsed.Count==2&&loanParsed[0].IsLoan&&loanParsed[0].IsLoanToBuy&&loanParsed[1].IsLoan&&!loanParsed[1].IsLoanToBuy,"Transfermarkt loan fee detection");
        string history="""<table><tbody><tr><td>30/06/2027</td><td><a title='Club A' href='/a/verein/1'>A</a></td><td><a title='Club B' href='/b/verein/2'>B</a></td><td>Fin du prêt</td></tr></tbody></table>""";
        Require(source.ParseLoanEndDate(history,"Club B","Club A")==new DateOnly(2027,6,30),"Transfermarkt loan end history and reverse clubs");
        string gridHistory="""<div class='tm-player-transfer-history-grid'><div class='tm-player-transfer-history-grid__season'>26/27</div><div class='tm-player-transfer-history-grid__date'>01/07/2027</div><div><a title='Club B' href='/b/verein/2'>B</a></div><div><a title='Club A' href='/a/verein/1'>A</a></div><div>Fin du prêt</div></div>""";
        Require(source.ParseLoanEndDate(gridHistory,"Club A","Club B")==new DateOnly(2027,7,1),"Transfermarkt CSS grid loan history");
        string apiHistory="""{"data":{"clubIds":["273","1164"],"history":{"terminated":[{"transferSource":{"clubId":"273"},"transferDestination":{"clubId":"1164"},"details":{"date":"2026-07-30T00:00:00+02:00"},"typeDetails":{"type":"ACTIVE_LOAN_TRANSFER"}}],"pending":[{"transferSource":{"clubId":"1164"},"transferDestination":{"clubId":"273"},"details":{"date":"2027-06-30T00:00:00+02:00"},"typeDetails":{"type":"RETURNED_FROM_PREVIOUS_LOAN"}}]}}} """;
        string apiClubs="""{"data":[{"id":"273","name":"Stade Rennais FC","baseDetails":{"shortName":"Stade Rennais","abbreviation":"SRFC"}},{"id":"1164","name":"Le Mans FC","baseDetails":{"shortName":"Le Mans FC","abbreviation":"LMFC"}}]}""";
        Require(source.ParseApiLoanEndDate(apiHistory,apiClubs,"Stade Rennais","Le Mans FC")==new DateOnly(2027,6,30),"Transfermarkt API pairs active loan with reversed pending return");
        Require(source.ParseApiLoanEndDate(apiHistory,apiClubs,"Le Mans FC","Stade Rennais") is null,"Transfermarkt API rejects the wrong initial direction");
        Console.WriteLine("PASS FC26 date conversion, 33 URLs, parser, loan detection/history, multi-league sequence preservation");

        var doc=DatabaseDocument.Open(Path.Combine(root,"files/fifa_ng_db.db"),Path.Combine(root,"files/fifa_ng_db-meta.xml"));
        var catalog=new FootballCatalog(doc);
        catalog.LoadNationalTeams(Path.Combine(root,"files/FC26_NATIONAL_TEAM_IDS.csv"));
        var groupLink=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid")))
            .GroupBy(r=>FootballCatalog.Value(r,"playerid")).First(g=>g.Count()==1&&catalog.Rows("players").Any(p=>FootballCatalog.Value(p,"playerid")==g.Key));
        var link=groupLink.Single();var id=groupLink.Key;
        string original=FootballCatalog.Value(link,"teamid");
        var destinations=catalog.Rows("teams").Select(r=>FootballCatalog.Value(r,"teamid"))
            .Where(t=>t!=original&&!catalog.NationalTeamIds.Contains(t)).Take(2).ToArray();
        var playerRow=catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==id);
        var nationalBefore=catalog.Rows("teamplayerlinks").Where(r=>catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToDictionary(r=>r,r=>r.ItemArray.ToArray());
        var plan=NativeTransferBatch.Preview(catalog,[
            new(1,id,original),new(2,id,destinations[0]),new(3,id,destinations[1])]);
        Require(plan.Steps[0].AlreadyThere&&plan.Steps[2].SourceId==destinations[0],"Virtual sequential state");
        Require(FootballCatalog.Value(link,"teamid")==original,"Preview must not mutate");
        plan.Apply();
        Require(FootballCatalog.Value(link,"teamid")==destinations[1],"Final destination");
        Require(FootballCatalog.Value(playerRow,"contractvaliduntil")=="2030","Blank contract becomes 2030");
        string assigned=FootballCatalog.Value(link,"jerseynumber");
        Require(int.TryParse(assigned,out int jersey)&&jersey is >=1 and <=99,"Blank number becomes 1-99");
        Require(SquadJerseys(catalog,destinations[1]).Count(n=>n==assigned)==1,"Assigned number is unique in the destination squad");
        foreach(var pair in nationalBefore)Require(pair.Key.ItemArray.SequenceEqual(pair.Value),"National links unchanged");
        var output=Path.Combine(root,"artifacts/ttlive-integration");Directory.CreateDirectory(output);
        string saved=Path.Combine(output,"native-transfer-"+Guid.NewGuid().ToString("N")+".db");
        doc.SaveAs(saved);
        var reopened=DatabaseDocument.Open(saved,Path.Combine(root,"files/fifa_ng_db-meta.xml"));
        Require(new FootballCatalog(reopened).Rows("teamplayerlinks").Any(r=>FootballCatalog.Value(r,"playerid")==id&&FootballCatalog.Value(r,"teamid")==destinations[1]),"Persisted transfer");
        var noOp=NativeTransferBatch.Preview(catalog,[new(1,id,destinations[1])]);noOp.Apply();Require(noOp.Steps[0].AlreadyThere,"Already-there no-op");
        Require(FootballCatalog.Value(playerRow,"contractvaliduntil")=="2030"&&FootballCatalog.Value(link,"jerseynumber")==assigned,"Already-there blank edits preserve contract/jersey");
        bool failed=false;
        try{NativeTransferBatch.Preview(catalog,[new(1,id,original),new(2,id,"999999999")]);}catch(InvalidDataException){failed=true;}
        Require(failed&&FootballCatalog.Value(link,"teamid")==destinations[1],"Invalid batch must not partially apply");
        var stale=NativeTransferBatch.Preview(catalog,[new(1,id,original)]);
        link["teamid"]=destinations[0];failed=false;
        try{stale.Apply();}catch(InvalidOperationException){failed=true;}
        Require(failed&&FootballCatalog.Value(link,"teamid")==destinations[0],"Stale preview");
        failed=false;
        try{NativeTransferBatch.Preview(catalog,[new(1,id,catalog.NationalTeamIds.First())]);}catch(InvalidDataException){failed=true;}
        Require(failed,"National destination prohibited");
        foreach(string tableName in new[]{"playerloans","career_presignedcontract"})
        {
            var table=doc.Tables.FirstOrDefault(t=>t.Name==tableName);
            if(table is null)continue;
            var temporary=TableEditing.Add(table);temporary["playerid"]=id;
            var otherId=catalog.Rows("players").Select(r=>FootballCatalog.Value(r,"playerid"))
                .First(candidate=>candidate!=id&&!catalog.Rows(tableName).Any(r=>FootballCatalog.Value(r,"playerid")==candidate));
            var other=TableEditing.Add(table);other["playerid"]=otherId;
            string currentClub=FootballCatalog.Value(link,"teamid");
            var unchanged=NativeTransferBatch.Preview(catalog,[new(1,id,currentClub)]);
            unchanged.Apply();
            Require(temporary.RowState!=DataRowState.Deleted&&temporary.RowState!=DataRowState.Detached,"Already-there must retain obligations");
            foreach(var targetState in new[]{DataRowState.Added,DataRowState.Unchanged,DataRowState.Modified})
            {
                if(targetState!=DataRowState.Added)temporary.AcceptChanges();
                if(targetState==DataRowState.Modified)
                    temporary[tableName=="playerloans"?"teamidloanedfrom":"offeredfee"]="77";
                var cancellation=NativeTransferBatch.Preview(catalog,[new(1,id,original)]);
                var values=temporary.ItemArray.ToArray();
                var state=temporary.RowState;
                bool throwOnce=true;
                DataColumnChangeEventHandler failWrite=(_,e)=>
                {
                    if(throwOnce&&ReferenceEquals(e.Row,link)&&e.Column!.ColumnName=="teamid")
                    {throwOnce=false;throw new InvalidOperationException("Injected write failure");}
                };
                link.Table.ColumnChanging+=failWrite;
                failed=false;
                try{cancellation.Apply();}catch(InvalidOperationException){failed=true;}
                finally{link.Table.ColumnChanging-=failWrite;}
                Require(failed&&temporary.ItemArray.SequenceEqual(values)&&temporary.RowState==state &&
                    FootballCatalog.Value(link,"teamid")==currentClub,$"Failure must restore {targetState} obligations and original club");
            }
            var staleObligation=NativeTransferBatch.Preview(catalog,[new(1,id,original)]);
            temporary["playerid"]="499998";failed=false;
            try{staleObligation.Apply();}catch(InvalidOperationException){failed=true;}
            Require(failed&&FootballCatalog.Value(link,"teamid")==currentClub,"Changed obligations invalidate preview");
            temporary["playerid"]=id;
            var finalPlan=NativeTransferBatch.Preview(catalog,[new(1,id,original),new(2,id,destinations[0])]);
            finalPlan.Apply();
            Require(!catalog.Rows(tableName).Any(r=>FootballCatalog.Value(r,"playerid")==id),"Moved player's obligation removed");
            Require(catalog.Rows(tableName).Contains(other),"Other players' obligations preserved");
            Require(FootballCatalog.Value(link,"teamid")==destinations[0],"Sequential transfer after cancellation");
            Console.WriteLine($"PASS {tableName}: cancellation, no-op preservation, stale guard, rollback, unrelated player preservation");
        }
        var occupant=catalog.Rows("teamplayerlinks").First(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))
            &&FootballCatalog.Value(r,"playerid")!=id&&FootballCatalog.Value(r,"teamid")!=FootballCatalog.Value(link,"teamid")
            &&int.TryParse(FootballCatalog.Value(r,"jerseynumber"),out int existing)&&existing is >=1 and <=99);
        string occupied=FootballCatalog.Value(occupant,"jerseynumber"),occupiedClub=FootballCatalog.Value(occupant,"teamid");
        NativeTransferBatch.Preview(catalog,[new NativeTransferEdit(1,id,occupiedClub,null,occupied)],new Random(7)).Apply();
        Require(FootballCatalog.Value(link,"teamid")==occupiedClub&&FootballCatalog.Value(link,"jerseynumber")==occupied,"Requested number is assigned");
        string displaced=FootballCatalog.Value(occupant,"jerseynumber");
        Require(int.TryParse(displaced,out int free)&&free is >=1 and <=99&&displaced!=occupied,"Occupant receives a free 1-99");
        var destNumbers=SquadJerseys(catalog,occupiedClub);
        Require(destNumbers.Count(n=>n==occupied)==1&&destNumbers.Count(n=>n==displaced)==1,"Requested and displaced numbers stay unique");
        PlayerTransfer.Apply(catalog,id,destinations[0],"2031");
        Require(FootballCatalog.Value(playerRow,"contractvaliduntil")=="2031","PlayerTransfer keeps an explicit contract");
        string transferred=FootballCatalog.Value(link,"jerseynumber");
        Require(int.TryParse(transferred,out int shirt)&&shirt is >=1 and <=99&&SquadJerseys(catalog,destinations[0]).Count(n=>n==transferred)==1,
            "PlayerTransfer without a number assigns a free shirt");
        string loanSource=FootballCatalog.Value(link,"teamid"),contractBeforeLoan=FootballCatalog.Value(playerRow,"contractvaliduntil");
        string transfermarktLoanSource=destinations[1];
        NativeTransferBatch.Preview(catalog,[new NativeTransferEdit(1,id,original,null,null,true,true,new DateOnly(2027,6,30),transfermarktLoanSource)]).Apply();
        var createdLoan=catalog.Rows("playerloans").Single(r=>FootballCatalog.Value(r,"playerid")==id);
        Require(FootballCatalog.Value(link,"teamid")==original&&loanSource!=transfermarktLoanSource&&FootballCatalog.Value(createdLoan,"teamidloanedfrom")==transfermarktLoanSource&&
            FootballCatalog.Value(createdLoan,"loandateend")=="162427"&&FootballCatalog.Value(createdLoan,"isloantobuy")=="1","Native loan fields");
        Require(FootballCatalog.Value(playerRow,"contractvaliduntil")==contractBeforeLoan,"Loan preserves player contract");
        PlayerTransfer.Apply(catalog,id,destinations[0],"2031");
        Require(!catalog.Rows("playerloans").Any(r=>FootballCatalog.Value(r,"playerid")==id),"Definitive transfer cancels the loan");
        Console.WriteLine("PASS native ordered apply, already-there, default contract/jersey, number displacement, national protection, atomic validation, stale preview and DB save/reopen");

        var cancelledFile=Path.Combine(output,"cancelled-relations-"+Guid.NewGuid().ToString("N")+".db");
        doc.SaveAs(cancelledFile);
        var cancelledCatalog=new FootballCatalog(DatabaseDocument.Open(cancelledFile,Path.Combine(root,"files/fifa_ng_db-meta.xml")));
        foreach(string tableName in new[]{"playerloans","career_presignedcontract"})
            Require(!cancelledCatalog.Rows(tableName).Any(r=>FootballCatalog.Value(r,"playerid")==id),"Cancellation persists after save/reopen");
        Console.WriteLine("PASS cancellations persist after DB save/reopen");

        File.WriteAllText(Path.Combine(output,"native-teams.json"),JsonSerializer.Serialize(catalog.Rows("teams").Select(r=>new { id=FootballCatalog.Value(r,"teamid"),name=FootballCatalog.Value(r,"teamname") }),Json));
        var live=new TransferLiveResolver(catalog,EnrichedPlayers.Load(Path.Combine(root,"files/players_enrich.json")));
        var report=new List<object>();
        foreach(var group in fixture.RootElement.GetProperty("groups").EnumerateArray())
        {
            var inputs=group.GetProperty("cases").EnumerateArray().Select(row=>new MarketTransfer(row.GetProperty("sequence").GetInt32(),
                row.GetProperty("player").GetString()!,row.GetProperty("oldClub").GetString()!,row.GetProperty("newClub").GetString()!,"")).ToArray();
            var result=live.Resolve(inputs);
            File.WriteAllText(Path.Combine(output,$"unresolved-{group.GetProperty("name").GetString()}.json"),JsonSerializer.Serialize(result.Unresolved,Json));
            report.Add(new { league=group.GetProperty("name").GetString(),total=inputs.Length,resolved=result.Resolved.Count,
                reasons=result.Unresolved.GroupBy(t=>t.Reason).ToDictionary(g=>g.Key,g=>g.Count()) });
        }
        File.WriteAllText(Path.Combine(output,"real-atlink-replay.json"),JsonSerializer.Serialize(report,Json));
        Console.WriteLine("PASS replay against ATLink's own enriched file and open native database");
        await Task.CompletedTask;
    }
}
