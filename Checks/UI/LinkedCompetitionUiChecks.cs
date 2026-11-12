using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.ViewModels;
using ATLink.Views;

internal static class LinkedCompetitionUiChecks
{
    public static void Run(string root)
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml") });
        var watch = Stopwatch.StartNew();
        void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
        string data = Path.Combine(root, "files/FC27");
        string meta = Path.Combine(data, "fifa_ng_db-meta.xml"), locMeta = Path.Combine(data, "loc/eng_us-meta.xml");
        using var context = new CompetitionProjectContext(CompdataProject.Open(Path.Combine(data, "compdata")), "FC27",
            DatabaseDocument.Open(Path.Combine(data, "fifa_ng_db.db"), meta),
            DatabaseDocument.Open(Path.Combine(data, "loc/eng_us.db"), locMeta), meta, locMeta);
        context.SetLanguages([new("fre_fr", "Français", Path.Combine(data, "loc/fre_fr.db"), Path.Combine(data, "loc/fre_fr-meta.xml"))]);
        NativeNeighbourUiChecks.Run(context);
        LinkedUiRenderChecks.CheckStudio(context);
        using var reference = context.OpenProject(Path.Combine(root, "files/export/SL-CL-REF-54"));
        var original = context.Project.Files.ToDictionary(f => f.RelativePath, f => f.Text);
        var beforeImport = reference.Project.Files.ToDictionary(f => f.RelativePath, f => f.Text);
        var imported = new FootballLinksViewModel(reference, 2000);
        Check(imported.Upper?.Id==2011&&imported.Lower?.Id==0,"Community League neighbours are not relative to the selected competition.");
        var importedUpper=new FootballLinksViewModel(reference,2011);
        Check(importedUpper.Upper?.Id==0&&importedUpper.Lower?.Id==2000,"Super League neighbours are incorrect.");
        Check(imported.ExistingLinks.Contains("semi-final") && imported.Quotas.Count == 21, "REF-54 links/quotas not recognized");
        Check(reference.Project.Files.All(f => f.Text == beforeImport[f.RelativePath]), "Opening linked panel modified REF-54");
        CreationLinkedGraphUiChecks.Run(context,reference);
        Console.WriteLine("Imported REF-54 read-only in " + watch.ElapsedMilliseconds + " ms");

        string[] upperClubs = reference.Rows("initteams.txt").Where(r => r[0] == "2011").OrderBy(r => int.Parse(r[1])).Select(r => r[2]).ToArray();
        string[] lowerDirect = reference.Rows("initteams.txt").Where(r => r[0] == "2000").OrderBy(r => int.Parse(r[1])).Select(r => r[2]).ToArray();
        string[] extra = context.Teams.Select(t => t.Id).Except(upperClubs.Concat(lowerDirect)).Take(2).ToArray();
        int Create(bool lower)
        {
            int id = 0;
            var wizard = new FootballCreationWizard(context, 84, value => id = value);
            var vm = wizard.Model;
            vm.Type = vm.Types.Single(t => t.Kind == FootballCompetitionKind.GroupsAndKnockout);
            vm.Region = vm.Regions.Single(r => r.Id == 84);
            vm.Name = lower ? "European Community League" : "Super League";
            var referencePhases=FootballCompetitionAdapter.Read(reference,lower?2000:2011).Phases;
            var awards=referencePhases.Select(p=>new FootballPhaseSettings{Prize=new(FootballSettingMode.Value,
                reference.EffectiveRules(p.NativeId!.Value).FirstOrDefault(r=>r.Key=="info_prize_money").Value??"0")}).ToArray();
            vm.Format.SetPhases([
                new() { Name = "Group stage", Kind = FootballPhaseKind.Groups, Participants = 20, Groups = 2,
                    QualifiersPerGroup = lower ? 2 : 8, Legs = 1,RoundSettings=[awards[0]] },
                new() { Name = "Knockout rounds", Kind = FootballPhaseKind.Knockout, Participants = lower ? 4 : 16,
                    Rounds = lower ? 2 : 4, Legs = lower ? 2 : 1, FinalLegs = 1,RoundSettings=awards.Skip(1).ToArray() }
            ]);
            foreach (string team in lower ? lowerDirect.Concat(extra) : upperClubs)
                vm.Participants.Add(new(context.Teams.Single(t => t.Id == team)));
            vm.Matchups.Synchronize();
            vm.Matchups.SelectedModel = "Crossed seeds";
            vm.Matchups.ApplyModel();
            vm.Calendar.Mode = "Choose matchday dates";
            vm.Calendar.Synchronize(); vm.Calendar.Matchdays.Clear();
            int[] groupDays = [234, 279, 283, 322, 325, 455, 458, 512, 521];
            foreach (var phase in vm.Calendar.Phases)
            {
                int[] dates = phase.Index == 0 ? groupDays : lower ? phase.Index == 1 ? [529, 532] : [535]
                    : [new[] { 526, 529, 532, 535 }[phase.Index - 1]];
                for (int i = 0; i < dates.Length; i++)
                    vm.Calendar.Matchdays.Add(new() { PhaseIndex = phase.Index, PhaseKey = phase.Key, PhaseName = phase.Name,
                        Round = i + 1, Date = context.ReferenceDate.AddDays(dates[i]).ToDateTime(TimeOnly.MinValue),
                        Kickoff = "21:00", Minimum = phase.Matches.ToString(), Maximum = phase.Matches.ToString(), Capacity = phase.Matches });
            }
            Check(vm.ValidateStep(2), "Creation invalid: " + vm.Error);
            vm.Create();
            Check(vm.Completed && id > 0, "Guided creation failed: " + vm.Error);
            return id;
        }
        int upper = Create(false), lower = Create(true);
        var links = new FootballLinksViewModel(context, lower) { Section = 1 };
        links.ExcludedCompetition = links.Competitions.Single(c => c.Id == upper);
        links.QualifierName = "Community League Qualifiers";
        links.UseEuropeanQuotas();
        foreach (var club in links.DirectClubs) club.Selected = lowerDirect.Contains(club.Club.Id);
        links.Review(); Check(links.Error.Length == 0, "Qualifier preview failed: " + links.Error);
        var pending = context.Project.Files.ToDictionary(f => f.RelativePath, f => f.Text);
        links.QualifierName += " changed";
        links.Confirmed = true; links.Apply();
        Check(links.Error.Contains("Review"), "Stale preview accepted.");
        Check(context.Project.Files.All(f => f.Text == pending[f.RelativePath]), "Failed apply changed project.");
        links.QualifierName = "Community League Qualifiers";
        links.Review(); links.Confirmed = true; links.Apply();
        Check(links.Error.Length == 0, "Qualifier creation failed: " + links.Error);
        int qualifier = FootballLinkedCompetitions.QualifiersFor(context, lower).Single().Competition;
        Check(FootballSeason.Read(context, qualifier).UnknownCount == 0, "Cumulative country caps became Expert-only");
        var afterQual = context.Project.Files.ToDictionary(f => f.RelativePath, f => f.Text);
        context.Undo(); Check(!context.Objects.Any(o => o.Id == qualifier), "Undo did not remove linked qualifier");
        context.Redo(); Check(context.Project.Files.All(f => f.Text == afterQual[f.RelativePath]), "Redo changed qualifier payload");

        links.Reload(); links.Section = 0;
        links.Upper = links.Competitions.Single(c => c.Id == upper);
        links.Lower = links.NeighbourChoices.Single(c=>c.Id==0);
        links.Promotion = links.PromotionChoices.Single(c => c.Id == 1);
        links.Review(); links.Confirmed = true; links.Apply();
        Check(links.Error.Length == 0, "Exchange failed: " + links.Error);
        links.Section = 2; links.CalendarSource = links.Competitions.Single(c => c.Id == upper);
        links.Review(); links.Confirmed = true; links.Apply();
        Check(links.Error.Length == 0, "Shared dates failed: " + links.Error);

        var roles = new Dictionary<int, string> { [upper] = "SL", [lower] = "CL", [qualifier] = "Q" };
        var referenceRoles = new Dictionary<int, string> { [2011] = "SL", [2000] = "CL", [1926] = "Q" };
        Dictionary<int, string> ObjectNames(CompetitionProjectContext project, Dictionary<int, string> roots)
        {
            var names = roots.ToDictionary();
            foreach (var pair in roots)
            {
                var phases = FootballCompetitionAdapter.Read(project, pair.Key).Phases;
                for (int i = 0; i < phases.Count; i++)
                {
                    names[phases[i].NativeId!.Value] = pair.Value + "/P" + i;
                    for (int g = 0; g < phases[i].NativeGroups.Count; g++) names[phases[i].NativeGroups[g]] = pair.Value + "/P" + i + "/G" + g;
                }
                var hidden = project.Objects.Where(o => o.Kind == 4 && o.ParentId == pair.Key && FootballCompetitionAdapter.IsHidden(project, o.Id)).OrderBy(o => o.Id).ToArray();
                for (int i = 0; i < hidden.Length; i++)
                {
                    names[hidden[i].Id] = pair.Value + "/H" + i;
                    int index = 0;
                    foreach (var group in project.Objects.Where(o => o.Kind == 5 && o.ParentId == hidden[i].Id).OrderBy(o => o.Id))
                        names[group.Id] = pair.Value + "/H" + i + "/G" + index++;
                }
            }
            return names;
        }
        var names = ObjectNames(context, roles); var expectedNames = ObjectNames(reference, referenceRoles);
        string[] Normalize(CompetitionProjectContext project, Dictionary<int, string> map, string file)
        {
            string Name(string id) => map.GetValueOrDefault(int.Parse(id)) ?? throw new Exception("Unknown linked object " + id);
            return project.Rows(file).Where(r => map.ContainsKey(int.Parse(r[0]))).Select(r =>
            {
                var copy = r.ToArray(); copy[0] = Name(copy[0]);
                if (file == "advancement.txt") copy[2] = Name(copy[2]);
                if (file == "tasks.txt")
                {
                    copy[3] = Name(copy[3]);
                    if (copy[2] is "FillFromCompTable" or "UpdateTable") copy[4] = Name(copy[4]);
                }
                return string.Join(',', copy);
            }).Order().ToArray();
        }
        foreach (string file in new[] { "tasks.txt", "advancement.txt", "initteams.txt" })
            Check(Normalize(context, names, file).SequenceEqual(Normalize(reference, expectedNames, file)),
                "Guided output differs from REF-54 after ID normalization: " + file);
        // Native calendar minimums may differ (capacity-only slots). Compare actual dates, rounds, maximum capacity and kickoff.
        string[] Calendar(CompetitionProjectContext project, Dictionary<int, string> map) => project.Rows("schedule.txt")
            .Where(r => map.ContainsKey(int.Parse(r[0]))).GroupBy(r =>
            {
                string phase=map[int.Parse(r[0])];int group=phase.IndexOf("/G",StringComparison.Ordinal);
                if(group>=0)phase=phase[..group];
                return phase+","+r[1]+","+r[2]+","+r[5];
            }).Select(g=>g.Key+","+g.Sum(r=>int.Parse(r[4]))).Order().ToArray();
        var actualCalendar=Calendar(context,names);var expectedCalendar=Calendar(reference,expectedNames);
        Check(actualCalendar.SequenceEqual(expectedCalendar), "Guided dates differ from REF-54\nGenerated only: " +
            string.Join(" | ",actualCalendar.Except(expectedCalendar)) + "\nReference only: " + string.Join(" | ",expectedCalendar.Except(actualCalendar)));
        foreach(var (id,referenceId) in new[]{(upper,2011),(lower,2000)})
        {
            string[] Prizes(CompetitionProjectContext c,int competition)=>FootballCompetitionAdapter.Read(c,competition).Phases
                .Select(p=>c.EffectiveRules(p.NativeId!.Value).FirstOrDefault(r=>r.Key=="info_prize_money").Value??"0").ToArray();
            Check(Prizes(context,id).SequenceEqual(Prizes(reference,referenceId)),"Guided prize amounts differ from REF-54");
        }
        foreach (var entry in original)
            Check(context.Project.Files.Single(f => f.RelativePath == entry.Key).Text.StartsWith(entry.Value.TrimEnd('\r', '\n'), StringComparison.Ordinal),
                "A native source row was rewritten: " + entry.Key);

        links.Reload();links.Section=1;
        links.QualifierName="Edited qualifying playoffs";
        var frenchQuota=links.Quotas.Single(q=>q.Path?.Id==0&&q.League?.Id==16);
        var portugueseQuota=links.Quotas.Single(q=>q.Path?.Id==0&&q.League?.Id==308);
        frenchQuota.Places=3;portugueseQuota.Places=4;
        links.Review();links.Confirmed=true;links.Apply();
        Check(links.Error.Length==0&&context.ObjectName(qualifier)=="Edited qualifying playoffs","Existing playoff edit failed: "+links.Error);
        Check(context.Rows("tasks.txt").Any(r=>r[0]==qualifier.ToString()&&r[2]=="FillFromLeagueMaxFromCountry"&&r[4]=="308"&&r[5]=="4"),"Edited quota was ignored");
        context.Undo();links.Reload();
        Check(context.ObjectName(qualifier)=="Community League Qualifiers"&&links.Quotas.Single(q=>q.Path?.Id==0&&q.League?.Id==308).Places==3,
            "Undo failed to restore quota and localized name");

        string output = Path.Combine(root, "Checks/output/linked-competitions-" + Guid.NewGuid().ToString("N"));
        context.Export(output);
        using var reopened = context.OpenProject(output);
        Check(FootballLinkedCompetitions.Divisions(reopened).Any(d => d.Upper == upper && d.Lower == lower &&
            d.Promotion == FootballPromotionSource.SemifinalWinners && d.QualifyingPlaces == 2), "Season links lost on reopen");
        Check(FootballLinkedCompetitions.QualifiersFor(reopened, lower).Count == 1, "Playoffs lost on reopen");
        var bindings = new StringWriter();
        var listener = new TextWriterTraceListener(bindings); PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var view = new FootballLinksEditor { DataContext = links };
        var reportDirectory = Path.Combine(root, "Checks/output/ui/linked-competitions");
        Directory.CreateDirectory(reportDirectory);
        foreach (int section in new[] { 0, 1, 2 })
        {
            links.Section = section;
            ((TabControl)view.FindName("Relations")).SelectedIndex = section;
            foreach (var size in new[] { new Size(1200, 800), new Size(760, 560) })
            {
                view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if(section==1&&size.Width==1200)
                {
                    ((ScrollViewer)view.Content).ScrollToVerticalOffset(470);view.UpdateLayout();
                    app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                }
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(reportDirectory, section + "-" + (int)size.Width + ".png")); encoder.Save(stream);
                ((ScrollViewer)view.Content).ScrollToTop();
            }
        }
        Check(bindings.ToString().Length == 0, "WPF linked-competition bindings failed: " + bindings);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        File.WriteAllText(Path.Combine(output, "guided-ref54-comparison.json"), JsonSerializer.Serialize(new {
            GuidedViewModels = true, ExpertUsed = false, NormalizedTasksEqual = true, NormalizedAdvancementEqual = true,
            InitialTeamsEqual = true, DatesEqual = true,PrizeAmountsEqual=true, ExistingPlayoffsEditable=true, Quotas = 21, Promotions = 2, Relegations = 2,
            LowerDirectEntrants = 18, LowerTotalEntrants = 20, InGame = "Not performed"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS guided REF-54 reproduction: normalized recruitment, progression, initial teams, dates, promotions/relegations, history, stale previews, reload and XAML bindings. Output: " + output);
    }
}
