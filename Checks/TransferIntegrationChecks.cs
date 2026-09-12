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
        var resolver=new TransferLiveResolver(EnrichedPlayers.FromPlayers(players),clubs,names);
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
        using var source=new TransfermarktSource(new TransferLog(),names);
        var parsed=source.ParseHtml(File.ReadAllText(Path.Combine(root,"Checks/Fixtures/ttlive-sample.html")));
        Require(parsed.Count>0&&parsed.First().PlayerName=="Kevin De Bruyne","TTLive parser names");
        var combined=TransferImport.Combine([parsed,parsed]);
        Require(combined.Count==parsed.Count*2&&combined.Select(t=>t.Sequence).SequenceEqual(Enumerable.Range(1,combined.Count)),"Preserve movements and number multiple leagues");
        Require(!source.IsTransfermarktUrl("https://transfermarkt.evil.test/"),"Host allowlist");
        Console.WriteLine("PASS 33 URLs, parser, multi-league sequence preservation");

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
