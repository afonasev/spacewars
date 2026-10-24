using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool campaignReinforced,campaignMineDiscovered,campaignMineDestroyed,campaignFork,campaignWest,campaignEast,campaignGate,campaignFinalReinforced,campaignVictory,campaignFirstCombat,campaignStartedRadio,campaignLegacyRadioNoHistory,campaignTutorialPauseHeld;
        private int campaignTutorialSeen,campaignTutorialCompleted,campaignTutorialQueued=-1;
        private readonly System.Collections.Generic.List<string> campaignRadioEvents=new System.Collections.Generic.List<string>();
        private readonly bool[] campaignGuardStopPending=new bool[4];
        private readonly byte[] campaignGuards=new byte[4]; // 0 dormant, 1 engaged, 2 returning
        private CampaignMissionView CampaignProjection()=>offline?.CampaignOpening==true?new CampaignMissionView(campaignReinforced,campaignMineDiscovered,campaignMineDestroyed,campaignFork,campaignWest,campaignEast,campaignGate,campaignFinalReinforced,campaignVictory,campaignTutorialSeen,campaignTutorialCompleted,campaignTutorialQueued,campaignTutorialPauseHeld,campaignFirstCombat,campaignRadioEvents.Select(CampaignRadioText).ToArray()):null;
        internal bool CampaignTutorialPauseHeld=>campaignTutorialPauseHeld;
        private bool CampaignTutorialEligible(int topic)=>topic==7||topic==0||topic==1&&(campaignTutorialCompleted&1)!=0||topic==2&&campaignReinforced||topic==3&&campaignFirstCombat||topic==4&&campaignMineDestroyed||topic==5&&campaignFork||topic==6&&campaignFinalReinforced;
        internal void ApplyCampaignTutorialControl(CampaignTutorialControl control)
        {
            if(offline?.CampaignOpening!=true||control==null)throw new ArgumentException("Campaign tutorial control requires its mission host.");
            switch(control.Kind)
            {
                case CampaignTutorialControlKind.Show:
                    if(control.Topic<0||control.Topic>7)throw new ArgumentException("Invalid campaign tutorial topic.");
                    if(!CampaignTutorialEligible(control.Topic)||(campaignTutorialCompleted&(1<<control.Topic))!=0)return;
                    if(campaignTutorialQueued!=-1&&campaignTutorialQueued!=control.Topic)return;
                    campaignTutorialSeen|=1<<control.Topic;campaignTutorialQueued=control.Topic;campaignTutorialPauseHeld=true;break;
                case CampaignTutorialControlKind.Dismiss:
                    if(control.Topic<0||control.Topic>7)throw new ArgumentException("Invalid campaign tutorial topic.");
                    if(campaignTutorialQueued==control.Topic){campaignTutorialQueued=-1;campaignTutorialPauseHeld=false;}break;
                case CampaignTutorialControlKind.Complete:
                    if(control.Topic<0||control.Topic>7)throw new ArgumentException("Invalid campaign tutorial completion.");
                    campaignTutorialSeen|=1<<control.Topic;campaignTutorialCompleted|=1<<control.Topic;if(campaignTutorialQueued==control.Topic){campaignTutorialQueued=-1;campaignTutorialPauseHeld=false;}break;
                case CampaignTutorialControlKind.SuspendForDevice:
                    if(campaignTutorialQueued>=0&&campaignTutorialQueued!=7)campaignTutorialPauseHeld=false;break;
                case CampaignTutorialControlKind.ResumeForDevice:
                    if(campaignTutorialQueued>=0)campaignTutorialPauseHeld=true;break;
                default:throw new ArgumentException("Unknown campaign tutorial control.");
            }
        }
        private static readonly string[] CampaignRadioOrder={"mission-start","reinforcement-3","mine-visible-vector","mine-visible-control","mine-destroyed","first-combat","fork","branch-west","branch-east","gate-open","reinforcement-4","victory"};
        private static string CampaignRadioText(string id)
        {
            switch(id){case "mission-start":return "Контроль: Вектор-1, добро пожаловать во Внешние миры. Ваш отряд ждёт у старого геодезического поста.";case "reinforcement-3":return "Дозор: Командир, три машины исправны. Переходим под ваше управление.";case "mine-visible-vector":return "Вектор-1: Вижу работающую установку. Маркировка конкурента.";case "mine-visible-control":return "Контроль: Разрешений на добычу здесь нет. Остановите работу.";case "mine-destroyed":return "Контроль: Установка отключена. Продолжайте разведку. Дальше возможно вооружённое охранение.";case "first-combat":return "Вектор-1: Две боевые машины в проходе. Открыли огонь!";case "fork":return "Контроль: Впереди два объекта: карьер и производственный пост. Зачистите оба. Порядок выбирайте сами.";case "branch-west":case "branch-east":return "Вектор-1: Сектор чист. Выдвигаемся ко второму объекту.";case "gate-open":return "Контроль: Подкрепление вышло к восточному сборному пункту. Соединитесь перед дальнейшим продвижением.";case "reinforcement-4":return "Дозор: Четыре машины на связи. За перевалом основной комплекс. Подтверждаем тяжёлую охрану.";case "victory":return "Контроль: Добыча остановлена, вооружённое сопротивление подавлено. Территория под нашим контролем. Хорошая работа, офицер.";default:throw new ArgumentException("Unknown campaign radio event.");}
        }
        private void CampaignRadioEmitOnce(string id){if(!campaignRadioEvents.Contains(id))campaignRadioEvents.Add(id);}
        internal int CampaignCheckpointStage=>offline?.CampaignOpening!=true?0:campaignFinalReinforced?2:campaignReinforced?1:0;
        internal bool CampaignNeedsCheckpoint=>CampaignCheckpointStage>0;
        private bool CampaignTransferred(Unit u)=>offline?.CampaignOpening==true&&u.Owner==PlayableOwner.Player&&(u.ScenarioIndex>=1&&u.ScenarioIndex<=3&&campaignReinforced||u.ScenarioIndex>=4&&u.ScenarioIndex<=7&&campaignFinalReinforced);
        private bool CampaignDeferred(int index)=>offline.CampaignOpening&&index>=4&&index<=7;
        private int CampaignGuard(Unit u)
        {if(offline?.CampaignOpening!=true||u.Owner!=PlayableOwner.Enemy)return -1;for(int i=0;i<4;i++)if(CampaignReconOpening.GuardIndices[i].Contains(u.ScenarioIndex))return i;return -1;}
        private bool CampaignMoveAllowed(int id,NavPoint goal)=>!units.TryGetValue(id,out var u)||CampaignGuard(u)<0||CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[CampaignGuard(u)],goal,PlayableUnitRules.Radius(profile,u.Kind));
        private void CampaignAggression(int id,PlayableOwner? source)
        {
            if(offline?.CampaignOpening!=true||source!=PlayableOwner.Player)return;
            if(units.TryGetValue(id,out var u)){int group=CampaignGuard(u);if(group>=0)ActivateCampaignGuard(group);}
            else if(buildings.TryGetValue(id,out var b)&&b.Owner==PlayableOwner.Enemy)for(int i=0;i<4;i++)if(CampaignReconOpening.GuardSites[i].Contains(b.SiteId))ActivateCampaignGuard(i);
        }
        private void ActivateCampaignGuard(int group)
        {if(campaignGuards[group]!=1){campaignGuards[group]=1;campaignGuardStopPending[group]=true;}}
        private void AdvanceCampaignGuards(double dt)
        {
            if(offline?.CampaignOpening!=true)return;
            for(int group=0;group<4;group++)
            {
                var members=units.Values.Where(u=>CampaignGuard(u)==group).OrderBy(u=>u.Id).ToArray();var region=CampaignReconOpening.GuardRegions[group];
                var protectedBuildings=buildings.Values.Where(b=>b.Owner==PlayableOwner.Enemy&&CampaignReconOpening.GuardSites[group].Contains(b.SiteId)).ToArray();
                // The region constrains guards, not targets. Local units/buildings supply legitimate contact;
                // an outside target remains attackable from any reachable firing point inside the region.
                var contacts=units.Values.Where(u=>u.Owner==PlayableOwner.Player&&TryActor(u.Id,out var n)&&VisibleTarget(PlayableOwner.Enemy,u.Id)&&
                    (members.Any(g=>TryActor(g.Id,out var guard)&&Distance(guard.Position,n.Position)<=PlayableUnitRules.Vision(profile,g.Kind)&&GroundFireClear(guard.Position,n.Position,0))||
                     protectedBuildings.Any(b=>Distance(b.Position,n.Position)<=PlayableVision.BuildingRadius(profile,b.Kind,b.Phase)&&GroundFireClear(b.Position,n.Position,0)))).OrderBy(u=>u.Id).ToArray();
                if(contacts.Length>0)ActivateCampaignGuard(group);
                else if(campaignGuards[group]==1){campaignGuards[group]=2;campaignGuardStopPending[group]=true;}
                // Cancel the previous installed/pending chase before admitting return orders. A refused stop
                // retains intent and the physical step is frozen below until the transaction can commit.
                if(campaignGuardStopPending[group]&&(members.Length==0||navigation.TryStopGroup(members.Select(u=>u.Id).ToArray(),false)))campaignGuardStopPending[group]=false;
                bool home=true;
                foreach(var u in members)
                {
                    if(!TryActor(u.Id,out var n))continue;
                    var origin=offline.ScenarioUnits[u.ScenarioIndex].Position;
                    if(campaignGuardStopPending[group]){InterruptBurst(u);u.Target=0;home=false;continue;}
                    if(campaignGuards[group]==1&&contacts.Length>0)
                    {
                        var target=contacts.OrderBy(c=>{TryActor(c.Id,out var t);return Distance(n.Position,t.Position);}).First();TryActor(target.Id,out var tn);
                        u.Target=target.Id;u.ExplicitTarget=false;
                        double brake=n.Radius+PlayableUnitRules.Speed(profile,u.Kind,UnitUpgraded(u))*dt;
                        if(n.Moving&&!CampaignReconOpening.Contains(region,n.Position,brake))
                        {campaignGuardStopPending[group]=true;InterruptBurst(u);u.Target=0;home=false;continue;}
                        if(Distance(n.Position,tn.Position)>PlayableUnitRules.Range(profile,u.Kind)&&!n.Moving&&!navigation.IsPending(u.Id)&&TryAttackApproach(u.Id,n.Position,tn.Position,out var approach))CombatMove(u.Id,approach);
                    }
                    else if(campaignGuards[group]==2)
                    {
                        InterruptBurst(u);u.Target=0;u.ExplicitTarget=false;
                        if(Distance(n.Position,origin)>n.Radius){home=false;if(!n.Moving&&!navigation.IsPending(u.Id))CombatMove(u.Id,origin);}
                        else if(n.Moving||navigation.IsPending(u.Id))CombatStop(u.Id);
                    }
                }
                if(campaignGuards[group]==2&&home){campaignGuards[group]=0;campaignGuardStopPending[group]=false;}
            }
        }
        private void StepNavigation(double dt)
        {
            if(offline?.CampaignOpening!=true){StepNavigationCore(dt);return;}
            var limited=new System.Collections.Generic.List<Tuple<int,double,double>>();
            try
            {
                foreach(var u in units.Values)
                {
                    int group=CampaignGuard(u);if(group<0||!TryActor(u.Id,out var n))continue;
                    var r=CampaignReconOpening.GuardRegions[group];double clearance=Math.Min(Math.Min(n.Position.X-r.MinX,r.MaxX-n.Position.X),Math.Min(n.Position.Z-r.MinZ,r.MaxZ-n.Position.Z))-n.Radius;
                    // Motion proposals are bounded by Speed*dt and joint resolution only refuses motion.
                    // A half-clearance cap keeps the whole footprint inside even during an installed detour or avoidance.
                    // The canonical mobility is restored before capture/publication; no balance/profile/save change.
                    double speed=campaignGuardStopPending[group]?0:Math.Min(n.Speed,Math.Max(0,clearance)/(2*dt));
                    if(speed<n.Speed){limited.Add(Tuple.Create(n.Id,n.Speed,n.TurnSpeed));navigation.Crowd.SetMobility(n.Id,speed,n.TurnSpeed);}
                }
                StepNavigationCore(dt);
            }
            finally{foreach(var entry in limited)navigation.Crowd.SetMobility(entry.Item1,entry.Item2,entry.Item3);}
        }
        private bool CampaignAt(NavPoint p)=>units.Values.Any(u=>u.Owner==PlayableOwner.Player&&TryActor(u.Id,out var n)&&Distance(n.Position,p)<=profile.ExplorerCollisionRadius);
        private bool CampaignGroupDestroyed(int group)=>!units.Values.Any(u=>CampaignGuard(u)==group)&&!buildings.Values.Any(b=>CampaignReconOpening.GuardSites[group].Contains(b.SiteId));
        private void TransferCampaignSquad(int low,int high)
        {
            foreach(var u in units.Values.Where(u=>u.Owner==PlayableOwner.Third&&u.ScenarioIndex>=low&&u.ScenarioIndex<=high).ToArray()){u.Owner=PlayableOwner.Player;TryActor(u.Id,out var n);n.Team=TeamOf(u.Owner);}
            RecordArmy(PlayableOwner.Third);RecordArmy(PlayableOwner.Player);RefreshVision();
        }
        private void AdvanceCampaignOpening()
        {
            if(!units.Values.Any(u=>u.Owner==PlayableOwner.Player&&u.Health>0)){eliminated.Add(PlayableOwner.Player);Outcome=PlayableMatchOutcome.PlayerLost;return;}
            if(!campaignStartedRadio){campaignStartedRadio=true;if(!campaignLegacyRadioNoHistory)CampaignRadioEmitOnce("mission-start");}
            if(!campaignReinforced&&CampaignAt(CampaignReconOpening.Rendezvous)){campaignReinforced=true;TransferCampaignSquad(1,3);CampaignRadioEmitOnce("reinforcement-3");}
            if(!campaignMineDiscovered&&campaignReinforced&&Vision(PlayableOwner.Player).IsVisible(CampaignReconOpening.Mine)){campaignMineDiscovered=true;CampaignRadioEmitOnce("mine-visible-vector");CampaignRadioEmitOnce("mine-visible-control");}
            if(!campaignMineDestroyed&&campaignMineDiscovered&&!buildings.Values.Any(b=>b.SiteId==1&&b.Kind==PlayableBuildingKind.Mine)){campaignMineDestroyed=true;CampaignRadioEmitOnce("mine-destroyed");}
            if(!campaignFirstCombat&&campaignMineDestroyed&&!campaignFork&&units.Values.Any(u=>u.Owner==PlayableOwner.Enemy&&u.Health>0&&VisibleTarget(PlayableOwner.Player,u.Id))){campaignFirstCombat=true;CampaignRadioEmitOnce("first-combat");}
            if(!campaignFork&&campaignMineDestroyed&&Vision(PlayableOwner.Player).IsVisible(CampaignReconOpening.Fork)){campaignFork=true;CampaignRadioEmitOnce("fork");}
            if(campaignFork){bool hadClearedBranch=campaignWest||campaignEast;campaignWest=CampaignGroupDestroyed(1);campaignEast=CampaignGroupDestroyed(2);if(!hadClearedBranch){if(campaignWest)CampaignRadioEmitOnce("branch-west");else if(campaignEast)CampaignRadioEmitOnce("branch-east");}}
            if(campaignWest&&campaignEast&&!campaignGate)
            {
                // Prepare all placements before publishing the gate transaction; refusal leaves the closed gate intact.
                var reserve=Enumerable.Range(4,4).Select(i=>offline.ScenarioUnits[i]).ToArray();
                if(reserve.All(e=>navigation.Crowd.CanPlace(e.Position,PlayableUnitRules.Radius(profile,e.Kind))))
                {
                    campaignGate=true;if(!TryRebuildGeometry()){campaignGate=false;return;}CampaignRadioEmitOnce("gate-open");
                    for(int i=4;i<=7;i++){var e=offline.ScenarioUnits[i];int id=SpawnUnit(e.Position,PlayableOwner.Third,e.Kind);units[id].ScenarioIndex=i;units[id].AuthoredUpgrade=e.Upgraded;TryActor(id,out var n);n.Heading=e.Heading;units[id].Turret=e.Heading;}
                }
            }
            if(campaignGate&&!campaignFinalReinforced&&CampaignAt(CampaignReconOpening.Convergence)){campaignFinalReinforced=true;TransferCampaignSquad(4,7);CampaignRadioEmitOnce("reinforcement-4");}
            if(!campaignVictory&&campaignFinalReinforced&&campaignMineDestroyed&&CampaignGroupDestroyed(0)&&CampaignGroupDestroyed(1)&&CampaignGroupDestroyed(2)&&CampaignGroupDestroyed(3)){campaignVictory=true;Outcome=PlayableMatchOutcome.PlayerWon;CampaignRadioEmitOnce("victory");}
        }
        private void WriteCampaignState(BinaryWriter w)
        {
            if(offline?.CampaignOpening!=true)return;
            w.Write(0x43414D33);WorldWire.String(w,CampaignReconOpening.Id);WorldWire.String(w,CampaignReconOpening.Revision);WorldWire.String(w,CampaignReconOpening.DefinitionHash);
            foreach(bool flag in new[]{campaignReinforced,campaignMineDiscovered,campaignMineDestroyed,campaignFork,campaignWest,campaignEast,campaignGate,campaignFinalReinforced,campaignVictory,campaignFirstCombat,campaignStartedRadio,campaignLegacyRadioNoHistory,campaignTutorialPauseHeld})w.Write(flag);
            w.Write(campaignTutorialSeen);w.Write(campaignTutorialCompleted);w.Write(campaignTutorialQueued);w.Write(campaignRadioEvents.Count);foreach(string id in campaignRadioEvents)WorldWire.String(w,id);
            foreach(byte state in campaignGuards)w.Write(state);foreach(bool pending in campaignGuardStopPending)w.Write(pending);
        }
        private void ReadCampaignState(BinaryReader r)
        {
            if(offline?.CampaignOpening!=true)return;
            int magic=r.ReadInt32();if((magic!=0x43414D32&&magic!=0x43414D33)||WorldWire.String(r)!=CampaignReconOpening.Id||WorldWire.String(r)!=CampaignReconOpening.Revision||WorldWire.String(r)!=CampaignReconOpening.DefinitionHash)throw new ArgumentException("Mission definition mismatch.");
            campaignReinforced=WorldWire.Boolean(r);campaignMineDiscovered=WorldWire.Boolean(r);campaignMineDestroyed=WorldWire.Boolean(r);campaignFork=WorldWire.Boolean(r);campaignWest=WorldWire.Boolean(r);campaignEast=WorldWire.Boolean(r);campaignGate=WorldWire.Boolean(r);campaignFinalReinforced=WorldWire.Boolean(r);campaignVictory=WorldWire.Boolean(r);
            if(magic==0x43414D33){campaignFirstCombat=WorldWire.Boolean(r);campaignStartedRadio=WorldWire.Boolean(r);campaignLegacyRadioNoHistory=WorldWire.Boolean(r);campaignTutorialPauseHeld=WorldWire.Boolean(r);campaignTutorialSeen=r.ReadInt32();campaignTutorialCompleted=r.ReadInt32();campaignTutorialQueued=r.ReadInt32();int count=r.ReadInt32();if(count<0||count>CampaignRadioOrder.Length)throw new ArgumentException("Invalid campaign radio history.");campaignRadioEvents.Clear();for(int i=0;i<count;i++)campaignRadioEvents.Add(WorldWire.String(r));}
            else{campaignFirstCombat=false;campaignStartedRadio=true;campaignLegacyRadioNoHistory=true;campaignTutorialPauseHeld=false;campaignTutorialSeen=campaignTutorialCompleted=0;campaignTutorialQueued=-1;campaignRadioEvents.Clear();}
            for(int i=0;i<4;i++)campaignGuards[i]=r.ReadByte();for(int i=0;i<4;i++)campaignGuardStopPending[i]=WorldWire.Boolean(r);
        }
        private void ValidateCampaignState()
        {
            if(campaignMineDiscovered&&!campaignReinforced||campaignMineDestroyed&&!campaignMineDiscovered||campaignFork&&!campaignMineDestroyed||
                (campaignWest||campaignEast)&&!campaignFork||campaignGate&&!(campaignWest&&campaignEast)||campaignFinalReinforced&&!campaignGate||campaignVictory&&!campaignFinalReinforced||
                WinnerTeam.HasValue||campaignGuards.Any(s=>s>2)||Enumerable.Range(0,4).Any(i=>campaignGuardStopPending[i]&&campaignGuards[i]==0)||
                !(Outcome==PlayableMatchOutcome.Playing||Outcome==PlayableMatchOutcome.PlayerLost||Outcome==PlayableMatchOutcome.PlayerWon||Outcome==PlayableMatchOutcome.ManuallyFinished)||
                (Outcome==PlayableMatchOutcome.PlayerWon)!=campaignVictory||buildings.Values.Any(b=>b.Owner==PlayableOwner.Player)||
                Outcome==PlayableMatchOutcome.Playing&&!units.Values.Any(u=>u.Owner==PlayableOwner.Player)||Outcome==PlayableMatchOutcome.PlayerLost&&units.Values.Any(u=>u.Owner==PlayableOwner.Player)||
                units.Values.Any(u=>u.ScenarioIndex>=1&&u.ScenarioIndex<=3&&u.Owner!=(campaignReinforced?PlayableOwner.Player:PlayableOwner.Third))||
                units.Values.Any(u=>u.ScenarioIndex>=4&&u.ScenarioIndex<=7&&(!campaignGate||u.Owner!=(campaignFinalReinforced?PlayableOwner.Player:PlayableOwner.Third)))||
                units.Values.Any(u=>CampaignGuard(u)>=0&&TryActor(u.Id,out var n)&&!CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[CampaignGuard(u)],n.Position,n.Radius))||
                campaignMineDestroyed&&buildings.Values.Any(b=>b.SiteId==1)||campaignWest&&!CampaignGroupDestroyed(1)||campaignEast&&!CampaignGroupDestroyed(2)||campaignVictory&&(!CampaignGroupDestroyed(0)||!CampaignGroupDestroyed(3))||
                ((campaignTutorialSeen|campaignTutorialCompleted)&~0xff)!=0||(campaignTutorialCompleted&~campaignTutorialSeen)!=0||campaignTutorialQueued< -1||campaignTutorialQueued>7||campaignTutorialPauseHeld&&campaignTutorialQueued<0||campaignTutorialQueued>=0&&((campaignTutorialSeen&(1<<campaignTutorialQueued))==0||(campaignTutorialCompleted&(1<<campaignTutorialQueued))!=0)||
                campaignFirstCombat&&!campaignMineDestroyed||campaignLegacyRadioNoHistory&&campaignRadioEvents.Contains("mission-start")||campaignStartedRadio&&!campaignLegacyRadioNoHistory&&!campaignRadioEvents.Contains("mission-start")||
                campaignRadioEvents.Count>CampaignRadioOrder.Length||campaignRadioEvents.Distinct().Count()!=campaignRadioEvents.Count||campaignRadioEvents.Any(id=>!CampaignRadioOrder.Contains(id))||!CampaignRadioEventsChronological()||
                campaignRadioEvents.Contains("reinforcement-3")&&!campaignReinforced||(campaignRadioEvents.Contains("mine-visible-vector")||campaignRadioEvents.Contains("mine-visible-control"))&&!campaignMineDiscovered||campaignRadioEvents.Contains("mine-destroyed")&&!campaignMineDestroyed||campaignRadioEvents.Contains("first-combat")&&!campaignFirstCombat||campaignRadioEvents.Contains("fork")&&!campaignFork||campaignRadioEvents.Contains("branch-west")&&!campaignWest||campaignRadioEvents.Contains("branch-east")&&!campaignEast||campaignRadioEvents.Contains("gate-open")&&!campaignGate||campaignRadioEvents.Contains("reinforcement-4")&&!campaignFinalReinforced||campaignRadioEvents.Contains("victory")&&!campaignVictory||
                campaignTutorialQueued>=0&&!CampaignTutorialEligible(campaignTutorialQueued))throw new ArgumentException("Invalid mission progression/outcome.");
        }
        private bool CampaignRadioEventsChronological(){int prior=-1;foreach(string id in campaignRadioEvents){int rank=id=="branch-east"?Array.IndexOf(CampaignRadioOrder,"branch-west"):Array.IndexOf(CampaignRadioOrder,id);if(rank<prior)return false;prior=rank;}return true;}
    }
    public sealed partial class PlayableRuntime
    {
        private readonly System.Collections.Generic.Queue<CampaignTutorialControl> campaignTutorialControls=new System.Collections.Generic.Queue<CampaignTutorialControl>();
        public bool RequestCampaignTutorialControl(long generation,CampaignTutorialControl control)
        {
            if(offlineConfiguration?.CampaignOpening!=true||control==null||generation!=Generation)return false;
            lock(gate){if(stopping!=0||stopped!=0||generation!=Generation||campaignTutorialControls.Count>=64)return false;campaignTutorialControls.Enqueue(control);}
            PresentationChanged();Signal();return true;
        }
        private void QueueCampaignTutorialControl(CampaignTutorialControl control)
        {if(offlineConfiguration?.CampaignOpening!=true)return;lock(gate)if(stopping==0&&stopped==0&&campaignTutorialControls.Count<64)campaignTutorialControls.Enqueue(control);}
        private void DrainCampaignTutorialControls()
        {
            CampaignTutorialControl[] controls;lock(gate){controls=campaignTutorialControls.ToArray();campaignTutorialControls.Clear();}
            foreach(var control in controls)domain.ApplyCampaignTutorialControl(control);
            bool changed;lock(gate){int held=domain.CampaignTutorialPauseHeld?1:0;campaignTutorialPauseHeld=held;int effective=menuPaused!=0||held!=0?1:0;changed=paused!=effective;Volatile.Write(ref paused,effective);}
            if(controls.Length>0||changed)PresentationChanged();
        }
        private void OnCampaignCommandApplied(PlayableCommand command,PlayableCommandStatus status)
        {
            if(status!=PlayableCommandStatus.Applied||offlineConfiguration?.CampaignOpening!=true)return;
            int topic=command.Kind==PlayableCommandKind.Move?1:command.Kind==PlayableCommandKind.Attack?3:command.Kind==PlayableCommandKind.AttackMove?4:-1;
            if(topic>=0)QueueCampaignTutorialControl(new CampaignTutorialControl(CampaignTutorialControlKind.Complete,topic));
        }
        public long HumanSequenceWatermark{get{lock(gate)return offlineConfiguration==null?lastAcceptedSequence:acceptedByOwner.TryGetValue("player-1",out var value)?value:0;}}
        private byte[] campaignCheckpoint;
        private Task<byte[]> campaignCheckpointRequest;
        private int campaignCheckpointStage,campaignCheckpointRequestedStage;
        public bool HasCampaignCheckpoint=>Volatile.Read(ref campaignCheckpoint)!=null;
        public byte[] CampaignCheckpoint{get{var bytes=Volatile.Read(ref campaignCheckpoint);return bytes==null?null:(byte[])bytes.Clone();}}
        private void MaintainCampaignCheckpoint()
        {
            if(offlineConfiguration?.CampaignOpening!=true||!domain.CampaignNeedsCheckpoint||campaignCheckpointStage>=domain.CampaignCheckpointStage)return;
            if(campaignCheckpointRequest!=null)
            {
                if(!campaignCheckpointRequest.IsCompleted)return;
                if(campaignCheckpointRequest.Status==TaskStatus.RanToCompletion){Volatile.Write(ref campaignCheckpoint,campaignCheckpointRequest.Result);campaignCheckpointStage=campaignCheckpointRequestedStage;}
                // A refused ingress barrier leaves the previous valid checkpoint untouched; retry at the next clean boundary.
                campaignCheckpointRequest=null;if(campaignCheckpointStage>=domain.CampaignCheckpointStage)return;
            }
            lock(gate)if(captureRequest==null&&inbox.Count==0&&humanActions.Count==0&&campaignTutorialControls.Count==0&&finishRequested==0&&requestedBalance==null&&requestedAiProfile==null)
            {campaignCheckpointRequestedStage=domain.CampaignCheckpointStage;captureRequest=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);campaignCheckpointRequest=captureRequest.Task;}
        }
    }
}
