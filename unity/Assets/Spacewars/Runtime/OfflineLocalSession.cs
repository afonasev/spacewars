using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed class LocalSeat
    {
        public string Id { get; }
        public string OwnerId { get; }
        public string DeviceId { get; }
        public bool Connected { get; internal set; }
        public bool Neutral { get; internal set; }
        public bool Ready { get; internal set; }
        internal long Sequence;
        internal LocalSeat(string id,string owner,string device) {Id=id;OwnerId=owner;DeviceId=device;}
    }

    // Session identity, focus and physical availability do not enter simulation.
    public sealed class OfflineLocalSession : IDisposable
    {
        private readonly PlayableRuntime runtime;
        private readonly LocalSeat[] seats;
        private long consumedOrdinal;
        private bool started;private string lostSeat;
        public IReadOnlyList<LocalSeat> Seats { get; }
        public bool Paused { get; private set; } = true;
        public string WaitingSeat => seats.FirstOrDefault(s=>!s.Connected)?.Id??(lostSeat!=null&&(!Seat(lostSeat).Neutral||!Seat(lostSeat).Ready)?lostSeat:seats.FirstOrDefault(s=>!s.Neutral)?.Id);
        public OfflinePresentationFrame Frame { get; private set; }
        public IReadOnlyDictionary<string,IReadOnlyList<PlayableCommandReceipt>> OwnerReceipts { get; private set; }
        public OfflineLocalSession(PlayableRuntime runtime,LocalSeat[] seats)
        {
            this.runtime=runtime??throw new ArgumentNullException(nameof(runtime));
            if(seats==null||seats.Length!=2||seats.Any(s=>s==null)||seats.Select(s=>s.Id).Distinct().Count()!=2||seats.Select(s=>s.OwnerId).Distinct().Count()!=2||seats.Select(s=>s.DeviceId).Distinct().Count()!=2)
                throw new ArgumentException("Exactly two independent local identities/devices required.");
            if(seats.Count(s=>Regex.IsMatch(s.DeviceId,@"^keyboard\+mouse:[0-9]+:[0-9]+$"))!=1||seats.Count(s=>Regex.IsMatch(s.DeviceId,@"^gamepad:[0-9]+$"))!=1)throw new ArgumentException("One keyboard/mouse and one gamepad binding required.");
            foreach(var seat in seats)if(!runtime.OfflineFrame.Views.ContainsKey(seat.OwnerId)||!runtime.OfflineFrame.Views[seat.OwnerId].Participants.Any(p=>p.Id==seat.OwnerId&&p.Control==OfflineControl.Human))throw new ArgumentException("Unknown human owner.");
            this.seats=(LocalSeat[])seats.Clone();Seats=Array.AsReadOnly(this.seats);runtime.RequestPause(true);ReadFrame();
        }
        public static LocalSeat Bind(string id,string owner,string device)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(owner)||string.IsNullOrWhiteSpace(device))throw new ArgumentException("Missing binding.");
            return new LocalSeat(id,owner,device);
        }
        private LocalSeat Seat(string id)=>seats.Single(s=>s.Id==id);
        public void SampleDevice(string id,bool connected,bool neutral)
        {
            var s=Seat(id);bool lost=s.Connected&&!connected;s.Connected=connected;
            if(!connected){lostSeat=s.Id;s.Ready=false;s.Neutral=false;}
            else if(neutral)s.Neutral=true;
            if(lost||!connected)Pause();
        }
        public void Ready(string id){var s=Seat(id);if(!s.Connected||!s.Neutral)throw new InvalidOperationException("Device must return to neutral.");s.Ready=true;}
        public bool Resume()
        {
            if(!seats.All(s=>s.Ready&&s.Connected&&s.Neutral))return false;
            started=true;lostSeat=null;Paused=false;runtime.RequestPause(false);return true;
        }
        public void Pause()
        {
            Paused=true;foreach(var seat in seats)seat.Neutral=false;runtime.RequestPause(true);
        }
        // The presentation receives exactly one captured reference and fans it out.
        public void ReadFrame()
        {
            var next=runtime.OfflineFrame;
            if(next.Receipts.Count>0&&next.Receipts[0].Ordinal>consumedOrdinal+1){Pause();throw new InvalidOperationException("Receipt fanout fell behind; fail closed.");}
            OwnerReceipts=seats.ToDictionary(s=>s.OwnerId,s=>(IReadOnlyList<PlayableCommandReceipt>)Array.AsReadOnly(next.Receipts.Where(r=>r.Ordinal>consumedOrdinal&&r.Receipt.OwnerId==s.OwnerId).Select(r=>r.Receipt).ToArray()));
            consumedOrdinal=next.ReceiptOrdinal;Frame=next;
            // Transport capacity is consumed once by the coordinator, never a viewport.
            runtime.DrainReceipts();
        }
        public PlayableCommandSubmitResult Submit(string seatId,PlayableCommandKind kind,int[] entities,NavPoint point=default(NavPoint),int targetId=0,int siteId=0,int slotId=0,int parentId=0,PlayableBuildingKind buildingKind=PlayableBuildingKind.Factory,PlayableEntityKind unitKind=PlayableEntityKind.Tank,PlayableResearchKind researchKind=PlayableResearchKind.TankChassis)
        {
            var seat=Seat(seatId);
            if(!started||Paused||!seat.Connected||!seat.Neutral)return new PlayableCommandSubmitResult(PlayableCommandStatus.Rejected);
            var view=Frame.Views[seat.OwnerId];
            if((entities??Array.Empty<int>()).Any(id=>!view.Entities.Any(e=>e.Id==id&&e.Owner==view.Owner)&&!view.Buildings.Any(b=>b.Id==id&&b.Owner==view.Owner)))return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidEntity);
            return runtime.TrySubmit(new PlayableCommand(Frame.Generation,++seat.Sequence,seat.OwnerId,kind,entities,point,targetId:targetId,siteId:siteId,slotId:slotId,parentId:parentId,buildingKind:buildingKind,unitKind:unitKind,researchKind:researchKind));
        }
        public void Dispose()=>runtime.RequestStop();
    }

    // Map markers have no authority/entity/HUD object reference. Shared union must
    // never smuggle allied private queues, targets, orders, accounts or camera rects.
    public sealed class OfflineMapMarker
    {
        public int Id { get; } public PlayableOwner Owner { get; } public NavPoint Position { get; } public string Kind { get; } public MapMarkState State {get;}
        internal OfflineMapMarker(int id,PlayableOwner owner,NavPoint point,string kind,MapMarkState state=MapMarkState.Ready){Id=id;Owner=owner;Position=point;Kind=kind;State=state;}
    }
    public static class OfflineMapProjection
    {
        public static IReadOnlyList<OfflineMapMarker> Personal(PlayableSnapshot view)=>Array.AsReadOnly(
            view.Entities.Select(e=>new OfflineMapMarker(e.Id,e.Owner,e.Position,e.Kind.ToString(),MapMarkState.Unit)).Concat(view.Buildings.Where(b=>b.Phase!=ConstructionPhase.Pending).Select(b=>new OfflineMapMarker(b.Id,b.Owner,b.Position,b.Kind.ToString())))
            .Concat(view.Vision.KnownBuildings.Where(b=>!view.Buildings.Any(l=>l.Id==b.Id)).Select(b=>new OfflineMapMarker(b.Id,b.Owner,b.Position,b.Kind.ToString(),MapMarkState.Memory))).ToArray());
        public static IReadOnlyList<OfflineMapMarker> Shared(OfflinePresentationFrame frame,IEnumerable<string> owners)=>Array.AsReadOnly(owners.SelectMany(owner=>Personal(frame.Views[owner])).GroupBy(m=>m.Id).Select(g=>g.OrderBy(m=>m.State==MapMarkState.Memory).First()).ToArray());
    }
}
