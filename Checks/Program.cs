using ATLink.Core;
if(args.Contains("--transfers")){await TransferIntegrationChecks.Run(Path.GetFullPath(args.FirstOrDefault(a=>a!="--transfers")??"."));return;}
if(args.Contains("--dbm")){DbmParityChecks.Run(Path.GetFullPath(args.FirstOrDefault(a=>a!="--dbm")??"."));return;}
if(args.Contains("--startup")){StartupChecks.Run(Path.GetFullPath(args.FirstOrDefault(a=>a!="--startup")??"."));return;}
if(args.Contains("--features")){ExtendedChecks.Run();return;}
var root=Path.GetFullPath(args.Length>0?args[0]:".");
var source=Path.Combine(root,"files/fifa_ng_db.db");
var meta=Path.Combine(root,"files/fifa_ng_db-meta.xml");
var doc=DatabaseDocument.Open(source,meta);
Console.WriteLine($"Loaded {doc.Tables.Count} tables");
if(TableOrdering.IdColumn(doc.Tables.Single(t=>t.Name=="players"))!="playerid"||TableOrdering.IdColumn(doc.Tables.Single(t=>t.Name=="teams"))!="teamid")throw new Exception("Primary ID columns");
var sortedTeams=new FootballCatalog(doc).Entities("teams");
if(sortedTeams.Count==0||sortedTeams.Zip(sortedTeams.Skip(1),(a,b)=>TableOrdering.NumericId(a.Id)<=TableOrdering.NumericId(b.Id)).Any(ok=>!ok))throw new Exception("Module lists not sorted by ID");
foreach(var name in new[]{"players","teams","teamplayerlinks","playernames"}){var t=doc.Tables.Single(t=>t.Name==name);Console.WriteLine($"{name}: {t.RowCount}, first: {string.Join(", ",t.Data.Rows[0].ItemArray.Take(4))}");}
var dir=Path.Combine(root,"Checks/output");Directory.CreateDirectory(dir);
var exact=Path.Combine(dir,Guid.NewGuid()+".db");doc.SaveAs(exact);
if(!File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(exact)))throw new Exception("Unchanged roundtrip differs");
Console.WriteLine("PASS byte-identical unchanged save");
var teams=doc.Tables.Single(t=>t.Name=="teams");
var row=teams.Data.Rows.Cast<System.Data.DataRow>().Single(r=>(string)r["teamid"]=="1");
row["teamname"]="ATLink validation";
var edited=Path.Combine(dir,Guid.NewGuid()+".db");doc.SaveAs(edited);
var reread=DatabaseDocument.Open(edited,meta);
if(!reread.Tables.Single(t=>t.Name=="teams").Data.Rows.Cast<System.Data.DataRow>().Any(r=>(string)r["teamid"]=="1"&&(string)r["teamname"]=="ATLink validation"))throw new Exception("Edit roundtrip failed");
Console.WriteLine("PASS modified name save and reread");
try{doc.SaveAs(source);throw new Exception("Source overwrite allowed");}catch(InvalidOperationException){Console.WriteLine("PASS source protected");}
row["teamid"]="99999999";
try{doc.SaveAs(Path.Combine(dir,Guid.NewGuid()+".db"));throw new Exception("Invalid value allowed");}catch(InvalidDataException){Console.WriteLine("PASS invalid integer rejected");}
File.Delete(exact);File.Delete(edited);reread=null!;GC.Collect();
// Exercise a rebuilt compressed table and compare every surviving value after decoding.
foreach(var t in doc.Tables)t.Data.RejectChanges();
var names=doc.Tables.Single(t=>t.Name=="playernames");
var nameRow=names.Data.Rows[1];nameRow["name"]="Élodie 中文 ⚽";
var added=TableEditing.Add(names,names.Data.Rows[2]);added["name"]="ATLink new name";
var removed=names.Data.Rows[3];removed.Delete();
var expected=names.Data.Rows.Cast<System.Data.DataRow>().Where(r=>r.RowState!=System.Data.DataRowState.Deleted).Select(r=>r.ItemArray.Select(v=>v?.ToString()??"").ToArray()).ToArray();
var expanded=Path.Combine(dir,Guid.NewGuid()+".db");doc.SaveAs(expanded);
var after=DatabaseDocument.Open(expanded,meta);var decoded=after.Tables.Single(t=>t.Name=="playernames");
if(decoded.RowCount!=expected.Length)throw new Exception("Rebuild row count mismatch");
for(int i=0;i<expected.Length;i++)if(!decoded.Data.Rows[i].ItemArray.Select(v=>v?.ToString()??"").SequenceEqual(expected[i]))throw new Exception($"Compressed rebuild mismatch row {i}");
foreach(var table in after.Tables.Where(t=>t.Name!="playernames"))
{
    var before=doc.Tables.Single(t=>t.Name==table.Name);
    if(before.RowCount!=table.RowCount)throw new Exception("Unchanged table count differs");
    for(int i=0;i<before.RowCount;i++)if(!before.Data.Rows[i].ItemArray.SequenceEqual(table.Data.Rows[i].ItemArray))throw new Exception($"Unchanged table modified: {table.Name}");
}
Console.WriteLine("PASS Unicode Huffman rewrite, addition/deletion, every decoded value and all other tables preserved");
var sample="a,b\r\n\"one,two\",\"line1\nline2\"\r\n";var parsed=TableEditing.Parse(sample);if(parsed.Count!=2||parsed[1][0]!="one,two"||parsed[1][1]!="line1\nline2")throw new Exception("CSV quoting failed");
Console.WriteLine("PASS quoted CSV and multiline values");
File.Delete(expanded);after=null!;decoded=null!;expected=null!;GC.Collect();
foreach(var t in doc.Tables)t.Data.RejectChanges();
var catalog=new FootballCatalog(doc);
var linkTable=doc.Tables.Single(t=>t.Name=="teamplayerlinks");
var single=linkTable.Data.Rows.Cast<System.Data.DataRow>().GroupBy(r=>(string)r["playerid"]).First(g=>g.Count()==1);
var clubLink=single.Single();string playerId=(string)clubLink["playerid"],sourceClub=(string)clubLink["teamid"];
var choices=doc.Tables.Single(t=>t.Name=="teams").Data.Rows.Cast<System.Data.DataRow>().Where(r=>(string)r["teamid"]!=sourceClub).Take(2).Select(r=>(string)r["teamid"]).ToArray();
var protectedLink=TableEditing.Add(linkTable,clubLink);protectedLink["teamid"]=choices[0];
var nationalFixture=Path.Combine(dir,Guid.NewGuid()+".csv");File.WriteAllText(nationalFixture,"teamid\n"+choices[0]+"\n");catalog.LoadNationalTeams(nationalFixture);
var previewTransfer=catalog.PreviewTransfer(playerId,choices[1]);catalog.ApplyTransfer(previewTransfer);
if((string)clubLink["teamid"]!=choices[1]||(string)protectedLink["teamid"]!=choices[0])throw new Exception("National link protection failed");
try{catalog.PreviewTransfer(playerId,choices[0]);throw new Exception("National destination allowed");}catch(InvalidOperationException){}
var ambiguous=TableEditing.Add(linkTable,clubLink);ambiguous["teamid"]=sourceClub;
try{catalog.PreviewTransfer(playerId,sourceClub);throw new Exception("Ambiguous source allowed");}catch(InvalidOperationException){}
Console.WriteLine("PASS transfer preview/apply, protected national relation, national destination and ambiguous source refusal");
File.Delete(nationalFixture);
var compFile=new CompdataFile{RelativePath="settings.txt",Original=[],Encoding=System.Text.Encoding.UTF8,InitialText="# keep comment\r\n1,rule,42\r\nunknown\r\n",Text="# keep comment\r\n1,rule,42\r\nunknown\r\n"};
var compTable=new CompdataTable(compFile);compTable.Data.Rows[0]["value"]="43";
if(compTable.Serialize()!="# keep comment\r\n1,rule,43\r\nunknown\r\n")throw new Exception("Compdata preservation failed");
Console.WriteLine("PASS Compdata comments, unknown lines and newline preservation");
// Batch transfer keeps duplicate player operations in their original sequence.
ambiguous.Delete();clubLink["teamid"]=sourceClub;
var batchCsv=$"sequence,playerid,new_teamid\n1,{playerId},{choices[1]}\n2,{playerId},{sourceClub}\n";
var batch=TransferBatch.Preview(catalog,batchCsv);if(batch.Steps.Count!=2||batch.Steps[1].Source!=batch.Steps[0].DestinationName)throw new Exception("Ordered transfer simulation failed");
batch.Apply(catalog);if((string)clubLink["teamid"]!=sourceClub||(string)protectedLink["teamid"]!=choices[0])throw new Exception("Batch application failed");
Console.WriteLine("PASS repeated-player transfer sequence and protected relation");
foreach(var t in doc.Tables)t.Data.RejectChanges();
var formation=new FormationEditor(new FootballCatalog(doc),"1");
string firstPlayer=formation.Slots[0].PlayerId,secondPlayer=formation.Slots[1].PlayerId;
formation.Slots[0].PlayerId=secondPlayer;formation.Slots[1].PlayerId=firstPlayer;
formation.Apply();
if((string)formation.Sheet["playerid0"]!=secondPlayer||(string)formation.Mentality["playerid0"]!=secondPlayer)throw new Exception("Lineup synchronization failed");
formation.Slots[1].PlayerId=secondPlayer;
try{formation.Apply();throw new Exception("Duplicate starter accepted");}catch(InvalidDataException){}
Console.WriteLine("PASS formation synchronization and duplicate player refusal");
CompdataFile Fixture(string path,string text)=>new(){RelativePath=path,Text=text,InitialText=text,Original=System.Text.Encoding.UTF8.GetBytes(text),Encoding=new System.Text.UTF8Encoding(false)};
var competitionProject=new CompdataProject{SourceFolder=dir,Files=new[]{Fixture("compobj.txt","1,1,world,World,0\n10,3,cup,Cup,1\n11,4,final,Final,10\n12,5,g,Group,11\n"),Fixture("compids.txt","10\n"),Fixture("settings.txt","# retained\n12,rule,1\n"),Fixture("standings.txt","12,1\n12,2\n")}};
var clone=CompetitionTools.PreviewClone(competitionProject,"10","C999","New cup");CompetitionTools.Apply(competitionProject,clone);
if(CompetitionTools.Choices(competitionProject).Count!=2||competitionProject.Validate().Count!=0||!competitionProject.Files.Single(f=>f.RelativePath=="settings.txt").Text.StartsWith("# retained\n"))throw new Exception("Competition clone failed");
Console.WriteLine("PASS competition subtree clone and reference remapping");
var realCatalog=new FootballCatalog(doc);realCatalog.LoadNationalTeams(Path.Combine(root,"files/FC26_NATIONAL_TEAM_IDS.csv"));
if(!realCatalog.NationalTeamIds.Contains("1318"))throw new Exception("Real semicolon national list was not loaded");
Console.WriteLine($"PASS real national list: {realCatalog.NationalTeamIds.Count} protected IDs");
string html="<div class='box'><h2>Club A</h2><table><thead><tr><th>Joueur</th><th>Allant à</th></tr></thead><tbody><tr><td><a href='/x/profil/spieler/1'>Player One</a></td><td>Club C</td></tr></tbody></table><table><thead><tr><th>Joueur</th><th>Venant de</th></tr></thead><tbody><tr><td><a href='/x/profil/spieler/1'>Player One</a></td><td>Club B</td></tr></tbody></table></div>";
var scraped=TransfermarktScraper.ParseHtml(html);if(scraped.Count!=2||scraped[0].OldClub!="Club B"||scraped[0].NewClub!="Club A"||scraped[1].NewClub!="Club C")throw new Exception("Arrival/departure ordering failed");
if(TransferResolver.Normalize("Élodie O’Neil")!="elodie o neil"||TransferResolver.Similarity("alpha","alpha")!=1)throw new Exception("Name normalization failed");
var realPlayer=realCatalog.Entities("players").First(p=>p.Id=="27");var resolver=new TransferResolver(realCatalog);
var resolved=resolver.Resolve(new[]{new MarketTransfer(1,realPlayer.Name,"Unrelated club","Sans club","")});
if(resolved[0].PlayerId!="27")throw new Exception("Unique name was blocked by old club");
var freeExists=realCatalog.Rows("teams").Any(r=>FootballCatalog.Value(r,"teamid")=="111592");
if(freeExists&&resolved[0].TeamId!="111592")throw new Exception("Free agent ID regression");
Console.WriteLine("PASS C# Transfermarkt parser, order, normalization and exact player resolution");
var enrich=EnrichedPlayers.Load(Path.Combine(root,"files/players_enrich.json"));
var jesse=enrich.Match("Jesse Bisiwu","FC Barcelona");
if(jesse?.PlayerId!="9009"||jesse.ShirtNumber!="27")throw new Exception("ATLink players_enrich.json Transfermarkt match failed");
if(CompetitionCatalog.Match("Premier League","England")?.TransfermarktCode!="GB1")throw new Exception("Premier League Transfermarkt URL mapping failed");
if(CompetitionCatalog.Match("Belgium Pro League (1)","Belgium")?.TransfermarktCode!="BE1")throw new Exception("Belgium Pro League Transfermarkt URL mapping failed");
if(!CompetitionCatalog.ForCatalog(realCatalog).Any(l=>l.TransfermarktUrl?.Contains("/wettbewerb/GB1",StringComparison.Ordinal)==true))throw new Exception("FC26 leagues were not linked to Transfermarkt URLs");
if(!CompetitionCatalog.ForCatalog(realCatalog).Any(l=>l.TransfermarktUrl=="https://www.transfermarkt.fr/jupiler-pro-league/transfers/wettbewerb/BE1"))throw new Exception("Belgium FC26 league was not linked to Transfermarkt BE1");
Console.WriteLine("PASS ATLink enrich matching and league Transfermarkt URLs");
var liveNames=new NameNormalizer();
var live=new TransferLiveResolver(realCatalog,enrich,liveNames);
var retired=live.Resolve([new MarketTransfer(1,"Jesse Bisiwu","FC Barcelona","Fin de carrière","")]);
if(retired.Resolved.Count!=0||retired.Unresolved.Count!=1||retired.Unresolved[0].Reason!=UnresolvedReasons.Retirement)
    throw new Exception("Retirement destination was not classified as retirement");
var jesseLive=live.Resolve([new MarketTransfer(1,"Jesse Bisiwu","FC Barcelona","Sans club","")]);
if(jesseLive.Resolved.Count!=1||jesseLive.Resolved[0].PlayerId!="9009"||jesseLive.Resolved[0].ShirtNumber!="27")
    throw new Exception("TransferLiveResolver Jesse Bisiwu match failed");
if(freeExists&&jesseLive.Resolved[0].ToTeamId!="111592")throw new Exception("TransferLiveResolver free agent ID regression");
var psg=new ClubNameResolver([new LiveTeam(73,"Paris Saint-Germain",false)],liveNames,LiveClubAliases.Load(liveNames));
if(psg.ResolveClub("PSG").TeamId!=73)throw new Exception("ClubNameResolver PSG alias failed");
if(psg.ResolveClub("Sans club").TeamId!=111592)throw new Exception("ClubNameResolver free agent alias failed");
var homonym=EnrichedPlayers.FromPlayers([
    new EnrichedPlayerInfo("1","John Smith","Same Club","10","10","Current A"),
    new EnrichedPlayerInfo("2","John Smith","Same Club","11","20","Current B")
]);
var homonymResolver=new TransferLiveResolver(homonym,new ClubNameResolver([],liveNames),liveNames);
var split=homonymResolver.Resolve([new MarketTransfer(1,"John Smith","Current A","Sans club","")]);
if(split.Resolved.Count!=1||split.Resolved[0].PlayerId!="1")throw new Exception("CurrentTeam did not disambiguate Transfermarkt homonyms");
var stillAmbiguous=homonymResolver.Resolve([new MarketTransfer(1,"John Smith","Same Club","Sans club","")]);
if(stillAmbiguous.Unresolved.Count!=1||stillAmbiguous.Unresolved[0].Reason!=UnresolvedReasons.AmbiguousPlayer)
    throw new Exception("Identical Transfermarkt clubs must stay ambiguous until CurrentTeam splits them");
if(!live.MatchesSourceClub("FC Barcelona","241","FC Barcelone"))throw new Exception("Transfermarkt FC Barcelona must match FC26 FC Barcelone");
if(live.MatchesSourceClub("FC Barcelona","73","Paris Saint-Germain"))throw new Exception("Transfermarkt old club must not match a different FC26 club");
Console.WriteLine("PASS TransferLiveResolver retirement, PSG/free-agent aliases, CurrentTeam homonyms");


// Generated tournaments: all team pairs, odd-team byes, full knockout progression and stale preview protection.
foreach(int count in new[]{3,4,5,16})
{
    var ids=Enumerable.Range(1,count).Select(i=>i.ToString()).ToArray();
    var fixtures=TournamentBuilder.RoundRobin(ids,true);
    if(fixtures.Count!=count*(count-1))throw new Exception("Round-robin fixture count");
    if(fixtures.Select(f=>(f.Home,f.Away)).Distinct().Count()!=fixtures.Count)throw new Exception("Repeated ordered fixture");
    if(fixtures.GroupBy(f=>f.Round).Any(g=>g.SelectMany(f=>new[]{f.Home,f.Away}).Distinct().Count()!=g.Count()*2))throw new Exception("Team scheduled twice in same round");
}
CompdataProject EmptyCompetition()=>new(){SourceFolder=dir,Files=new[]{Fixture("compobj.txt","0,0,W,World,-1\n1,2,ENG,England,0\n")}};
var draft=new TournamentDraft(1,"C1000","Test league",TournamentFormat.League,1,4,new[]{"1","2","3","4"},true,new DateOnly(2011,12,25),new DateOnly(2012,8,4),7,"15:00");
var generated=EmptyCompetition();var generatedPreview=TournamentBuilder.Preview(generated,draft);CompetitionTools.Apply(generated,generatedPreview);
if(generated.Validate().Count!=0||generated.Files.Single(f=>f.RelativePath=="schedule.txt").Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length!=6)throw new Exception("League calendar generation");
var cup=EmptyCompetition();CompetitionTools.Apply(cup,TournamentBuilder.Preview(cup,draft with{Format=TournamentFormat.Cup,TeamsPerGroup=16,Teams=Array.Empty<string>()}));
if(cup.Files.Single(f=>f.RelativePath=="advancement.txt").Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length!=30)throw new Exception("Knockout advancement count");
var stale=TournamentBuilder.Preview(generated,draft with{Code="C1001"});generated.Files.Single(f=>f.RelativePath=="compobj.txt").Text+="# external edit\n";
try{CompetitionTools.Apply(generated,stale);throw new Exception("Stale tournament applied");}catch(InvalidOperationException){}
Console.WriteLine("PASS new league/cup generation, odd/even round robins, return legs, advancement and stale previews");
var locPath=Path.Combine(root,"files/eng_us.db");
byte[] locCipher=File.ReadAllBytes(locPath);
if(!LocCrypto.IsEncrypted(locCipher))throw new Exception("Encrypted loc not detected");
if(!LocCrypto.Encrypt(LocCrypto.Decrypt(locCipher)).AsSpan().SequenceEqual(locCipher))throw new Exception("Loc AES roundtrip");
string locMeta=Path.Combine(root,"files/eng_us-meta.xml");
var loc=DatabaseDocument.Open(locPath,locMeta);
if(loc.Tables.Count!=2||!loc.Tables.Any(t=>t.Name=="LanguageStrings1")||!loc.Tables.Any(t=>t.Name=="LanguageStrings2"))throw new Exception("Loc table names");
if(!loc.Serialize().AsSpan().SequenceEqual(locCipher))throw new Exception("Unchanged loc cipher roundtrip");
bool HasText(DatabaseDocument doc,string needle)=>doc.Tables.Any(t=>t.Data.Rows.Cast<System.Data.DataRow>().Any(r=>(r["sourcetext"]?.ToString()??"").Contains(needle,StringComparison.OrdinalIgnoreCase)));
if(!HasText(loc,"Arsenal"))throw new Exception("English loc strings not decoded");
string locOut=Path.Combine(dir,Guid.NewGuid().ToString("N")+".db");
var locRow=loc.Tables.SelectMany(t=>t.Data.Rows.Cast<System.Data.DataRow>()).First(r=>(r["sourcetext"]?.ToString()??"").Contains("Arsenal",StringComparison.OrdinalIgnoreCase));
locRow["sourcetext"]="ATLinkLocTest";
loc.SaveAs(locOut);
if(!LocCrypto.IsEncrypted(File.ReadAllBytes(locOut)))throw new Exception("Edited loc not re-encrypted");
var locReread=DatabaseDocument.Open(locOut,locMeta);
if(!locReread.Tables.SelectMany(t=>t.Data.Rows.Cast<System.Data.DataRow>()).Any(r=>(string)r["sourcetext"]=="ATLinkLocTest"))throw new Exception("Loc edit not saved");
File.Delete(locOut);
string frPath=Path.Combine(root,"files/fre_fr.db"),frMeta=Path.Combine(root,"files/fre_fr-meta.xml");
if(!File.Exists(frPath)||!File.Exists(frMeta))throw new Exception("French loc files missing");
var fr=DatabaseDocument.Open(frPath,frMeta);
if(!HasText(fr,"France")&&!HasText(fr,"Équipe")&&!HasText(fr,"Ligue"))throw new Exception("French loc strings not decoded");
if(!fr.Serialize().AsSpan().SequenceEqual(File.ReadAllBytes(frPath)))throw new Exception("Unchanged French loc cipher roundtrip");
var csv=LocalizationFormat.ParseStrings("hashid,stringid,sourcetext\n0,TeamName_1,Arsenal\n");
if(csv[0][1]!="TeamName_1"||unchecked((int)LanguageHash.Compute("TeamName_1")).ToString()!=LocalizationFormat.ParseStrings("stringid,sourcetext\nTeamName_1,Arsenal\n")[0][0])throw new Exception("Loc CSV hash");
Console.WriteLine($"PASS loc AES-256-CBC decrypt/re-encrypt, EN {loc.Tables.Sum(t=>t.RowCount)} strings, FR {fr.Tables.Sum(t=>t.RowCount)} strings");
string squadPath=Path.Combine(root,"files/Squads20260905195257141");
var squad=SquadFile.Open(squadPath,meta);
if(squad.Database.Tables.Count!=83)throw new Exception("Squad table count");
if(!squad.Name.Contains("Squad Update",StringComparison.OrdinalIgnoreCase)||squad.Database.DisplayName!=squad.Name)throw new Exception("Squad save name");
byte[] squadBytes=File.ReadAllBytes(squadPath);
if(SquadFile.ReadName(squadBytes)!=squad.Name)throw new Exception("Squad name parse");
if(!squad.Database.Serialize().AsSpan().SequenceEqual(squadBytes))throw new Exception("Unchanged squad wrapper roundtrip");
var squadPlayers=squad.Database.Tables.Single(t=>t.Name=="players");
if(squadPlayers.RowCount==0||squadPlayers.SlotCapacity<=squadPlayers.RowCount)throw new Exception("Reserved squad slots not preserved");
var squadRow=squadPlayers.Data.Rows[0];string originalOverall=(string)squadRow["overallrating"];
squadRow["overallrating"]=originalOverall=="99"?"98":"99";
string squadOut=Path.Combine(dir,Guid.NewGuid().ToString("N")+".squad");squad.Database.SaveAs(squadOut);
var rereadSquad=SquadFile.Open(squadOut,meta);
if(!File.ReadAllBytes(squadOut).AsSpan(0,8).SequenceEqual("FBCHUNKS"u8))throw new Exception("Squad header lost");
if((string)rereadSquad.Database.Tables.Single(t=>t.Name=="players").Data.Rows[0]["overallrating"]!=(originalOverall=="99"?"98":"99"))throw new Exception("Squad edit not saved");
if(rereadSquad.Database.Tables.Single(t=>t.Name=="players").SlotCapacity!=squadPlayers.SlotCapacity)throw new Exception("Squad capacity lost");
if(rereadSquad.Name!=squad.Name)throw new Exception("Squad name lost on rewrite");
File.Delete(squadOut);Console.WriteLine($"PASS squad FBCHUNKS read/write, name '{squad.Name}', {squadPlayers.RowCount} active players, reserved slots kept");

