using System;

namespace Spacewars.Simulation
{
    internal struct NavMotionBody
    {
        internal int Id;
        internal int YieldLabel,OwnRouteIndex,OwnLocalIndex;
        internal bool OwnMoving;internal NavigationOutcome OwnOutcome;
        internal NavPoint Position,Target;
        internal double Heading,Radius,Speed,TurnSpeed;
        // Zero is the legacy/default sentinel. A zero voluntary cap already
        // makes Active false; receiver reach remains Speed until actual yield.
        internal double OwnPaceCap;
        // A certified local prefix ends here; forecasting straight beyond its
        // motor invents motion through the obstacle that prefix bends around.
        internal double LocalLookaheadSeconds;
        internal double OwnSpeed=>!Yielding&&OwnPaceCap>0?Math.Min(Speed,OwnPaceCap):Speed;
        internal double ReverseDistance,ReverseSpeedRatio,ReverseConeRadians;
        internal bool Active,Potential,Yielding,TerminalSeed;
    }
    internal struct NavMotionProposal
    {
        internal NavPoint Position,Velocity;
        internal double Heading;
        internal bool Blocked,Overflow,Refuse,StaticNoWinner;
        internal bool JointRefused; // Derived frame provenance, never saved authority.
        internal bool HasJointRefuser;
        internal int JointFirstRefuser; // Entity identity of the first actual cancellation in this frame.
        // Consumed before the second Joint; never wire or persistent state.
        internal bool IndependentTurn;
        internal bool RoleCapWait; // A physically qualified choice denied only by the frame's row cap.
        internal bool CurrentProgressExhausted; // No qualified eventual current-objective translation for this stationary turn.
    }
    internal interface INavMotionClearance { bool ClearMotion(int actor,NavPoint end); }

    // Stateless reachable sampling. Predictions rank choices; only the jointly
    // executed dt sweeps decide peer safety. No previous velocity is authority.
    internal static class NavCrowdMotion
    {
        // Disabled-by-default, owner-thread scalar evidence read/reset by existing QA callback.
        // No proposal/body/wire/storage layout changes and no gameplay consumer.
        internal struct CausalConflict
        {
            public int Pass,Actor,Peer;public NavPoint Position,PeerPosition,Velocity,PeerVelocity;
            public double ClosestSquared,RadiusSquared;
        }
        internal struct CausalFrame
        {
            public long ComputeSerial;public bool SelectedSeen,FinalSeen;public int JointPasses,ConflictCount,HintFlags;
            public bool PolicyRefused,TypedRefused;public NavPoint GuideTarget;public double ForwardLimit;
            public NavMotionBody Body;public NavMotionProposal Selected,PreFilter,Final;
            public CausalConflict First,Last;
            public CausalPeer FocusContext,Peer;
            public bool RetryAttempted,RetryAccepted,RetryPeerInitiallyZero,RetryPeerMatchedRefuser;
            public bool RetryGoalTurn;
            public int RetryPeerId,RetryPeerHintFlags;
            public NavMotionBody RetryPeerBody;public NavMotionProposal RetrySelected;
        }
        // Detached values only; populated at the existing joined capture sites.
        internal struct CausalPeer
        {
            public bool SelectedSeen,PreFilterSeen,FinalSeen,PreparedSeen,TypedSeen,NeighborMembershipKnown,HasOtherNeighbor,PolicyRefused,TypedRefused,Held;
            public int HintFlags,FinalHintFlags,TargetLeg,LocalLength,GlobalLength,NeighborCount,ConflictCount;
            public long World,Request,Group,Root,Activation,Entity,Incarnation,MemberOrder,Mobility;
            public NavPoint GuideTarget;public double ForwardLimit;
            public NavRecastStepState TypedState;
            public NavMotionBody Body,PreFilterBody,FinalBody;public NavMotionProposal Selected,PreFilter,Final;
            public CausalConflict First,Last;
        }
        [ThreadStatic] internal static long CausalSerial;
        [ThreadStatic] internal static int CausalActor;
        [ThreadStatic] internal static int CausalPeerActor;
        [ThreadStatic] internal static CausalFrame Causal;
        private static void ObserveCausalConflict(int actor,int peer,NavMotionBody[] bodies,NavMotionProposal[] proposals,double dt)
        {
            if(CausalActor==0||(bodies[actor].Id!=CausalActor&&(CausalPeerActor==0||bodies[actor].Id!=CausalPeerActor))||proposals[actor].Velocity.Equals(default(NavPoint)))return;
            var a=bodies[actor];var b=bodies[peer];var v=proposals[actor].Velocity;var w=proposals[peer].Velocity;double radius=a.Radius+b.Radius;
            var item=new CausalConflict{Pass=Causal.JointPasses,Actor=a.Id,Peer=b.Id,Position=a.Position,PeerPosition=b.Position,Velocity=v,PeerVelocity=w,ClosestSquared=ClosestSquared(Sub(a.Position,b.Position),Sub(v,w),dt),RadiusSquared=radius*radius};
            if(a.Id==CausalActor){if(Causal.ConflictCount++==0)Causal.First=item;Causal.Last=item;}
            else{if(Causal.Peer.ConflictCount++==0)Causal.Peer.First=item;Causal.Peer.Last=item;}
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Owner-thread causal control only. No authority state or gameplay setting.
        [ThreadStatic] internal static bool ForceAllOverflowComponentsEvidence;
        [ThreadStatic] internal static bool ForceDirectedNeighborsForQa,CountNeighborPairsForQa;
        [ThreadStatic] internal static long NeighborPairEvaluationsForQa;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [ThreadStatic] internal static bool DisableSelectClearanceReuseForQa;
        [ThreadStatic] internal static bool CountSelectClearanceForQa;
        [ThreadStatic] internal static bool ForceSmallClearanceMemoForTests;
        [ThreadStatic] internal static long SelectClearanceCallsForQa,SelectClearanceHitsForQa;
        [ThreadStatic] internal static bool DisableRefinementReuseForQa,CountRefinementForQa;
        [ThreadStatic] internal static long RefinementCallsForQa,RefinementIterationsForQa,RefinementReuseCallsForQa,RefinementRiskReuseCallsForQa;
        [ThreadStatic] internal static bool DisableRankingBeforeClearanceForTests;
        private static readonly bool OriginalSelectClearanceOrderForQa=Environment.GetEnvironmentVariable("SPACEWARS_ORIGINAL_SELECT_CLEARANCE_ORDER")=="1";
#endif
        // Only NavCrowd's audited production capability can opt in. Arbitrary
        // INavMotionClearance callbacks retain their original ordered call trace.
        private struct ClearanceEntry { internal long X,Z;internal bool Clear; }
        private static bool SelectClear(int row,NavPoint end,INavMotionClearance clearance,Span<ClearanceEntry> entries,ref int used,ref int next)
        {
            long x=BitConverter.DoubleToInt64Bits(end.X),z=BitConverter.DoubleToInt64Bits(end.Z);
            for(int i=0;i<used;i++)if(entries[i].X==x&&entries[i].Z==z){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(CountSelectClearanceForQa)SelectClearanceHitsForQa++;
#endif
                return entries[i].Clear;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(CountSelectClearanceForQa)SelectClearanceCallsForQa++;
#endif
            bool value=clearance.ClearMotion(row,end);
            if(entries.Length!=0){int at=used<entries.Length?used++:next;entries[at]=new ClearanceEntry{X=x,Z=z,Clear=value};next=(at+1)%entries.Length;}
            return value;
        }
        internal const int NeighborCapacity=32;
        internal static double Angle(double value){while(value>Math.PI)value-=2*Math.PI;while(value< -Math.PI)value+=2*Math.PI;return value;}
        internal static double Turn(double heading,double aim,double limit)=>Angle(heading+Math.Max(-limit,Math.Min(limit,Angle(aim-heading))));
        internal static double Length(NavPoint p)=>Math.Sqrt(p.X*p.X+p.Z*p.Z);
        private static NavPoint Sub(NavPoint a,NavPoint b)=>new NavPoint(a.X-b.X,a.Z-b.Z);
        private static NavPoint Velocity(double heading,double speed)=>new NavPoint(Math.Cos(heading)*speed,Math.Sin(heading)*speed);
        internal static double ClosestSquared(NavPoint separation,NavPoint relativeVelocity,double duration)
        {
            double q=relativeVelocity.X*relativeVelocity.X+relativeVelocity.Z*relativeVelocity.Z;
            double time=q==0?0:Math.Max(0,Math.Min(duration,-(separation.X*relativeVelocity.X+separation.Z*relativeVelocity.Z)/q));
            double x=separation.X+relativeVelocity.X*time,z=separation.Z+relativeVelocity.Z*time;return x*x+z*z;
        }
        private static NavPoint Preferred(in NavMotionBody a)
        {var delta=Sub(a.Target,a.Position);double length=Length(delta);return !a.Active||length==0?default:new NavPoint(delta.X/length*a.OwnSpeed,delta.Z/length*a.OwnSpeed);}
        private static double NeighborReach(in NavMotionBody a,in NavMotionBody b,NavigationProfile profile,double dt)
        {return a.Radius+b.Radius+((a.Active||a.Potential?a.Speed:0)+(b.Active||b.Potential?b.Speed:0))*Math.Max(dt,profile.CrowdLookaheadSeconds);}
        private static bool NeighborRelevant(in NavMotionBody a,in NavMotionBody b,NavigationProfile profile,double dt)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(CountNeighborPairsForQa)NeighborPairEvaluationsForQa++;
#endif
            var d=Sub(a.Position,b.Position);double reach=NeighborReach(in a,in b,profile,dt);return d.X*d.X+d.Z*d.Z<=reach*reach;
        }
        internal static void Neighbors(NavMotionBody[] bodies,int[] neighbors,int[] counts,NavigationProfile profile,double dt)
        {Neighbors(bodies,neighbors,counts,profile,dt,null);}
        internal static void Neighbors(NavMotionBody[] bodies,int[] neighbors,int[] counts,NavigationProfile profile,double dt,int[] components)
        {
            if(components!=null){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(!ForceDirectedNeighborsForQa){NeighborsWithComponents(bodies,neighbors,counts,profile,dt,components);return;}
#else
                NeighborsWithComponents(bodies,neighbors,counts,profile,dt,components);return;
#endif
                for(int i=0;i<bodies.Length;i++)components[i]=i;
            }
            for(int i=0;i<bodies.Length;i++){
                int count=0;for(int j=0;j<bodies.Length;j++)if(i!=j){
                    if(!NeighborRelevant(in bodies[i],in bodies[j],profile,dt))continue;
                    if(components!=null){int a=Root(components,i),b=Root(components,j);if(a!=b)components[b]=a;}
                    if(count<0)continue;
                    if(count==profile.CrowdNeighborCap){count=-1;if(components==null)break;continue;}
                    neighbors[i*NeighborCapacity+count++]=j;
                }counts[i]=count;
            }
            if(components!=null)for(int i=0;i<bodies.Length;i++)components[i]=Root(components,i);
        }
        private static void NeighborsWithComponents(NavMotionBody[] bodies,int[] neighbors,int[] counts,NavigationProfile profile,double dt,int[] components)
        {
            for(int i=0;i<bodies.Length;i++){components[i]=i;counts[i]=0;}
            // NeighborRelevant is symmetric: the squared separation and the two
            // radius/speed sums are unchanged when exchanging the frozen bodies.
            // Each row receives lower peers from earlier outer iterations, then
            // higher peers here, retaining the exact ascending row and cap prefix.
            // This is also the original first-encounter order for each union;
            // its later reverse encounter can only join an already joined pair.
            for(int i=0;i<bodies.Length;i++)for(int j=i+1;j<bodies.Length;j++){
                if(!NeighborRelevant(in bodies[i],in bodies[j],profile,dt))continue;
                int a=Root(components,i),b=Root(components,j);if(a!=b)components[b]=a;
                AppendNeighbor(neighbors,counts,i,j,profile.CrowdNeighborCap);
                AppendNeighbor(neighbors,counts,j,i,profile.CrowdNeighborCap);
            }
            for(int i=0;i<bodies.Length;i++)components[i]=Root(components,i);
        }
        private static void AppendNeighbor(int[] neighbors,int[] counts,int row,int peer,int cap)
        {
            int count=counts[row];if(count<0)return;
            if(count==cap){counts[row]=-1;return;}
            neighbors[row*NeighborCapacity+count]=peer;counts[row]=count+1;
        }
        internal static void ConstrainOverflowComponents(NavMotionBody[] bodies,int[] neighbors,int[] counts,int[] labels,double[] originalSpeeds,NavigationProfile profile,double dt)
        {
            for(int i=0;i<bodies.Length;i++)originalSpeeds[i]=bodies[i].Speed;
            bool hasOverflow=false;for(int i=0;i<bodies.Length;i++)if((bodies[i].Active||bodies[i].Potential)&&counts[i]<0){hasOverflow=true;break;}
            if(!hasOverflow)return; // Initial Neighbors already produced the exact rows; avoid a second all-pairs pass on ordinary frames.
            double horizon=profile==null?double.NaN:Math.Max(dt,profile.CrowdLookaheadSeconds);
            bool safe=profile!=null&&profile.CrowdNeighborCap>=0&&profile.CrowdNeighborCap<=NeighborCapacity&&Finite(dt)&&dt>0&&Finite(horizon)&&horizon>0;
            for(int i=0;i<bodies.Length&&safe;i++){
                var body=bodies[i];
                safe=Finite(body.Position.X)&&Finite(body.Position.Z)&&Finite(body.Radius)&&body.Radius>=0&&(!(body.Active||body.Potential)||Finite(originalSpeeds[i])&&originalSpeeds[i]>=0);
            }
            // A component with no active proposal cannot seed any displacement.
            // Certify that every positive receiver's reach covers twice the physical
            // support tolerance, so a support-contact chain cannot cross components.
            // Tiny caps and unusual input magnitudes keep the original full path.
            bool activeIslands=safe&&Finite(profile.ArrivalTolerance)&&profile.ArrivalTolerance>=0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            activeIslands&=!ForceAllOverflowComponentsEvidence;
#endif
            for(int i=0;i<bodies.Length&&activeIslands;i++)if(bodies[i].Active||bodies[i].Potential){
                double reach=originalSpeeds[i]*horizon;
                activeIslands=Finite(reach)&&reach>=2*profile.ArrivalTolerance;
            }
            bool handled=false;
            for(int root=0;root<bodies.Length;root++)if(labels[root]==root){
                if(activeIslands&&!ComponentHasActive(bodies,labels,root))continue;
                bool overflow=false;for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential)&&counts[i]<0){overflow=true;break;}
                if(!overflow)continue;
                handled=true;
                if(!safe||!TryComponentRatio(bodies,neighbors,labels,originalSpeeds,profile,dt,root,out double ratio))continue;
                bool scaleSafe=true;
                for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential)&&!TryScaledDown(originalSpeeds[i],ratio,out _)){scaleSafe=false;break;}
                if(!scaleSafe)continue;
                for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential))bodies[i].Speed=ScaledDown(originalSpeeds[i],ratio);
            }
            if(!handled)return; // No row scratch or speed was changed.
            // The row scratch was used transiently by the bounded order statistic.
            // Rebuild every exact row once after all initial components were handled.
            Neighbors(bodies,neighbors,counts,profile,dt);
            bool restored=false;
            for(int root=0;root<bodies.Length;root++)if(labels[root]==root){
                if(activeIslands&&!ComponentHasActive(bodies,labels,root))continue;
                bool overflow=false;for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential)&&counts[i]<0){overflow=true;break;}
                if(!overflow)continue;
                for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential))bodies[i].Speed=originalSpeeds[i];
                restored=true;
            }
            if(restored)Neighbors(bodies,neighbors,counts,profile,dt);
            if(!safe)return;
            // The uniform component bound is the conservative baseline. Reclaim
            // only existing profile speed bands whose exact incident pairs fit.
            // Complete original caps get first choice; then the existing lower
            // bands reclaim remaining capacity in stable entity/index order.
            bool reclaimed=false;
            for(int band=profile.CrowdSpeedBands;band>=1;band--){
            int previousId=int.MinValue,previousRow=-1;
            for(int order=0;order<bodies.Length;order++){
                int row=-1;for(int i=0;i<bodies.Length;i++)if((bodies[i].Id>previousId||bodies[i].Id==previousId&&i>previousRow)&&(row<0||bodies[i].Id<bodies[row].Id||bodies[i].Id==bodies[row].Id&&i<row))row=i;
                if(row<0)break;previousId=bodies[row].Id;previousRow=row;
                if(!(bodies[row].Active||bodies[row].Potential)||counts[row]<0||originalSpeeds[row]<=bodies[row].Speed)continue;
                var candidate=bodies[row];candidate.Speed=band==profile.CrowdSpeedBands?originalSpeeds[row]:originalSpeeds[row]*band/profile.CrowdSpeedBands;
                if(!Finite(candidate.Speed)||candidate.Speed<=bodies[row].Speed||candidate.Speed>originalSpeeds[row])continue;
                int count=0;bool fits=true;
                for(int peer=0;peer<bodies.Length;peer++)if(peer!=row&&NeighborRelevant(in candidate,in bodies[peer],profile,dt)){
                    if(++count>profile.CrowdNeighborCap){fits=false;break;}
                    if(!NeighborRelevant(in bodies[row],in bodies[peer],profile,dt)&&(bodies[peer].Active||bodies[peer].Potential)&&(counts[peer]<0||counts[peer]>=profile.CrowdNeighborCap)){fits=false;break;}
                }
                if(!fits)continue;
                for(int peer=0;peer<bodies.Length;peer++)if(peer!=row&&NeighborRelevant(in candidate,in bodies[peer],profile,dt)&&!NeighborRelevant(in bodies[row],in bodies[peer],profile,dt)&&counts[peer]>=0)counts[peer]=counts[peer]==profile.CrowdNeighborCap?-1:counts[peer]+1;
                bodies[row].Speed=candidate.Speed;counts[row]=count;reclaimed=true;
            }
            }
            // Preserve the ENTIRE full/band result as a floor before reclaiming
            // the remaining incident capacity with every other current speed fixed.
            reclaimed|=ReclaimIncidentCapacity(bodies,counts,originalSpeeds,profile,dt);
            if(reclaimed)Neighbors(bodies,neighbors,counts,profile,dt);
        }
        private static bool ReclaimIncidentCapacity(NavMotionBody[] bodies,int[] counts,double[] originalSpeeds,NavigationProfile profile,double dt)
        {
            bool reclaimed=false;int cap=profile.CrowdNeighborCap,previousId=int.MinValue,previousRow=-1;
            // Retain at most cap smallest new-edge boundaries; the next boundary
            // stays scalar. Scratch is private, so a no-gain row cannot corrupt rows.
            Span<double> smallest=stackalloc double[NeighborCapacity];
            for(int order=0;order<bodies.Length;order++){
                int row=-1;for(int i=0;i<bodies.Length;i++)if((bodies[i].Id>previousId||bodies[i].Id==previousId&&i>previousRow)&&(row<0||bodies[i].Id<bodies[row].Id||bodies[i].Id==bodies[row].Id&&i<row))row=i;
                if(row<0)break;previousId=bodies[row].Id;previousRow=row;
                var origin=bodies[row];double original=originalSpeeds[row];
                if(!(origin.Active||origin.Potential)||counts[row]<0||counts[row]>cap||original<=origin.Speed)continue;
                int slack=cap-counts[row],kept=0;double limit=original;
                for(int peer=0;peer<bodies.Length;peer++)if(peer!=row&&!NeighborRelevant(in origin,in bodies[peer],profile,dt)){
                    double boundary=FixedPeerBoundary(in origin,in bodies[peer],original,profile,dt);
                    // The (slack+1)th new edge would overflow the candidate row.
                    if(kept<slack){int at=kept++;while(at>0&&boundary<smallest[at-1]){smallest[at]=smallest[at-1];at--;}smallest[at]=boundary;}
                    else if(slack>0&&boundary<smallest[kept-1]){limit=Math.Min(limit,smallest[kept-1]);int at=kept-1;while(at>0&&boundary<smallest[at-1]){smallest[at]=smallest[at-1];at--;}smallest[at]=boundary;}
                    else limit=Math.Min(limit,boundary);
                    if((bodies[peer].Active||bodies[peer].Potential)&&(counts[peer]<0||counts[peer]>=cap))limit=Math.Min(limit,boundary);
                }
                if(!Finite(limit)||limit<=origin.Speed||limit>original)continue;
                var candidate=origin;candidate.Speed=limit;int count=0;bool fits=true;
                // Exact certificate includes every incident pair, even those
                // outside the current capped neighbor rows.
                for(int peer=0;peer<bodies.Length;peer++)if(peer!=row&&NeighborRelevant(in candidate,in bodies[peer],profile,dt)){
                    if(++count>cap){fits=false;break;}
                    if(!NeighborRelevant(in origin,in bodies[peer],profile,dt)&&(bodies[peer].Active||bodies[peer].Potential)&&(counts[peer]<0||counts[peer]>=cap)){fits=false;break;}
                }
                if(!fits)continue;
                for(int peer=0;peer<bodies.Length;peer++)if(peer!=row&&NeighborRelevant(in candidate,in bodies[peer],profile,dt)&&!NeighborRelevant(in origin,in bodies[peer],profile,dt)&&counts[peer]>=0)counts[peer]=counts[peer]==cap?-1:counts[peer]+1;
                bodies[row].Speed=limit;counts[row]=count;reclaimed=true;
            }
            return reclaimed;
        }
        private static double FixedPeerBoundary(in NavMotionBody origin,in NavMotionBody peer,double original,NavigationProfile profile,double dt)
        {
            var full=origin;full.Speed=original;
            if(!NeighborRelevant(in full,in peer,profile,dt))return original;
            // Caller proved absence at the current floor. Reuse the verified
            // lower-distance arithmetic of TryNonPermanentPairBoundary, then
            // subtract the peer's fixed CURRENT speed instead of scaling it.
            var separation=Sub(origin.Position,peer.Position);double d2=separation.X*separation.X+separation.Z*separation.Z;
            double radius=origin.Radius+peer.Radius,horizon=Math.Max(dt,profile.CrowdLookaheadSeconds);
            double lowerDistance=Math.Max(0,NextDown(Math.Sqrt(Math.Max(0,NextDown(d2)))));
            double lowerGap=Math.Max(0,NextDown(lowerDistance-radius));
            double lowerReachSpeed=Math.Max(0,NextDown(lowerGap/horizon));
            double fixedPeerSpeed=peer.Active||peer.Potential?peer.Speed:0;
            double boundary=Math.Max(origin.Speed,Math.Min(original,Math.Max(0,NextDown(lowerReachSpeed-fixedPeerSpeed))));
            var candidate=origin;candidate.Speed=boundary;
            return Finite(boundary)&&!NeighborRelevant(in candidate,in peer,profile,dt)?boundary:origin.Speed;
        }
        private static bool ComponentHasActive(NavMotionBody[] bodies,int[] labels,int root)
        {for(int row=0;row<bodies.Length;row++)if(labels[row]==root&&bodies[row].Active)return true;return false;}
        private static bool TryComponentRatio(NavMotionBody[] bodies,int[] neighbors,int[] labels,double[] originalSpeeds,NavigationProfile profile,double dt,int root,out double ratio)
        {
            ratio=1;bool found=false;
            for(int row=0;row<bodies.Length;row++)if(labels[row]==root&&(bodies[row].Active||bodies[row].Potential)){
                if(!TryRowRatio(bodies,neighbors,originalSpeeds,profile,dt,row,out double rowRatio))return false;
                ratio=Math.Min(ratio,rowRatio);found=true;
            }
            return found;
        }
        private static bool TryRowRatio(NavMotionBody[] bodies,int[] neighbors,double[] originalSpeeds,NavigationProfile profile,double dt,int row,out double ratio)
        {
            ratio=1;int cap=profile.CrowdNeighborCap,permanent=0;
            Span<int> permanentPeers=stackalloc int[NeighborCapacity];
            // At exact zero scale both effective speeds are zero for every
            // actor. Evaluate that same radius-only predicate directly; it has
            // no dependency on flags, speeds, dt or the lookahead horizon.
            ref readonly var origin=ref bodies[row];
            for(int peer=0;peer<bodies.Length;peer++)if(peer!=row){
                ref readonly var other=ref bodies[peer];
                double dx=origin.Position.X-other.Position.X,dz=origin.Position.Z-other.Position.Z;
                double radius=origin.Radius+other.Radius;
                if(!(dx*dx+dz*dz<=radius*radius))continue;
                if(permanent==cap)return false;
                permanentPeers[permanent++]=peer;
            }
            int kth=cap+1-permanent,removable=bodies.Length-1-permanent;
            if(kth>removable)return true;
            int keep=kth-1,kept=0,threshold=-1,offset=row*NeighborCapacity;
            // A row's inputs are immutable during this selection. Retain each
            // verified boundary alongside its peer rather than recomputing it
            // for every comparison and insertion. Scratch remains stack-local.
            Span<double> boundaries=stackalloc double[NeighborCapacity];
            double thresholdBoundary=0;
            int nextPermanent=0;
            for(int peer=0;peer<bodies.Length;peer++)if(peer!=row){
                // The ordered zero-scale results were frozen above for this row.
                if(nextPermanent<permanent&&peer==permanentPeers[nextPermanent]){nextPermanent++;continue;}
                if(!TryNonPermanentPairBoundary(bodies,originalSpeeds,row,peer,profile,dt,out double boundary))return false;
                // Once the retained kth boundary is below one, a verified one
                // cannot enter the retained row or replace its threshold. Skip
                // only that proven no-op, preserving even unused scratch slots.
                if(boundary==1&&threshold>=0&&thresholdBoundary<1)continue;
                if(keep==0){
                    if(threshold<0||CompareBoundary(boundary,peer,thresholdBoundary,threshold,bodies)<0){threshold=peer;thresholdBoundary=boundary;}
                    continue;
                }
                int last=kept<keep?-1:neighbors[offset+keep-1];double lastBoundary=last<0?0:boundaries[keep-1];
                bool enters=last<0||CompareBoundary(boundary,peer,lastBoundary,last,bodies)<0;
                if(enters){
                    int evicted=last,insert=kept<keep?kept:keep-1;
                    while(insert>0){
                        int previous=neighbors[offset+insert-1];
                        double previousBoundary=boundaries[insert-1];
                        if(CompareBoundary(boundary,peer,previousBoundary,previous,bodies)>=0)break;
                        if(insert<keep){neighbors[offset+insert]=previous;boundaries[insert]=previousBoundary;}
                        insert--;
                    }
                    neighbors[offset+insert]=peer;boundaries[insert]=boundary;if(kept<keep)kept++;
                    if(evicted>=0&&(threshold<0||CompareBoundary(lastBoundary,evicted,thresholdBoundary,threshold,bodies)<0)){threshold=evicted;thresholdBoundary=lastBoundary;}
                }else if(threshold<0||CompareBoundary(boundary,peer,thresholdBoundary,threshold,bodies)<0){threshold=peer;thresholdBoundary=boundary;}
            }
            if(threshold>=0){
                ratio=thresholdBoundary;
            }
            return true;
        }
        private static int CompareBoundary(double a,int peerA,double b,int peerB,NavMotionBody[] bodies)
        {
            int c=a.CompareTo(b);if(c!=0)return c;c=bodies[peerA].Id.CompareTo(bodies[peerB].Id);return c!=0?c:peerA.CompareTo(peerB);
        }
        private static bool TryNonPermanentPairBoundary(NavMotionBody[] bodies,double[] originalSpeeds,int row,int peer,NavigationProfile profile,double dt,out double boundary)
        {
            boundary=0;
            // Caller already proved the exact zero-scale predicate false.
            ref readonly var a=ref bodies[row];ref readonly var b=ref bodies[peer];double wi=a.Active||a.Potential?originalSpeeds[row]:0,wj=b.Active||b.Potential?originalSpeeds[peer]:0;
            var separation=Sub(a.Position,b.Position);double d2=separation.X*separation.X+separation.Z*separation.Z;
            double radius=a.Radius+b.Radius,horizon=Math.Max(dt,profile.CrowdLookaheadSeconds),speedSum=wi+wj;
            // In this bounded normal range all intermediates are finite and far
            // from underflow. Beyond twice the full-speed reach, conservative
            // ULP rounding still leaves the quotient above one: the exact result
            // below is one and its final full-speed neighbor check is false.
            // Other magnitudes retain the original verified arithmetic.
            double reach=radius+speedSum*horizon;
            if(horizon>=1d/1024&&horizon<=1024&&speedSum>=1d/1024&&speedSum<=1024&&radius>=0&&radius<=1024&&
                d2<=1099511627776d&&d2>4*reach*reach){boundary=1;return true;}
            if(!Finite(d2)||d2<0||!Finite(radius)||!Finite(horizon)||horizon<=0)return false;
            if(!Finite(speedSum)||speedSum<0)return false;
            if(speedSum==0){boundary=1;return true;}
            double distanceInput=Math.Max(0,NextDown(d2));double root=Math.Sqrt(distanceInput),lowerDistanceRaw=NextDown(root);
            if(!Finite(distanceInput)||!Finite(root)||!Finite(lowerDistanceRaw))return false;
            double lowerDistance=Math.Max(0,lowerDistanceRaw),lowerGapRaw=NextDown(lowerDistance-radius);
            if(!Finite(lowerGapRaw))return false;
            double lowerGap=Math.Max(0,lowerGapRaw),lowerSpeedSum=NextUp(speedSum);
            if(!Finite(lowerSpeedSum)||lowerSpeedSum<=0)return false;
            double lowerReachRaw=NextDown(lowerGap/horizon);
            if(!Finite(lowerReachRaw))return false;
            double lowerReachSpeed=Math.Max(0,lowerReachRaw),quotientRaw=NextDown(lowerReachSpeed/lowerSpeedSum);
            if(!Finite(quotientRaw))return false;
            double quotient=quotientRaw;
            double candidate=Math.Max(0,Math.Min(1,quotient));
            if(!NeighborAtScale(bodies,originalSpeeds,row,peer,candidate,profile,dt)){boundary=candidate;return true;}
            // q=0 was checked false above, so zero is the only fixed safe fallback.
            boundary=0;return true;
        }
        private static bool NeighborAtScale(NavMotionBody[] bodies,double[] originalSpeeds,int row,int peer,double ratio,NavigationProfile profile,double dt)
        {
            // Use the exact scaled-speed predicate without copying the full bodies.
            // Zero scale always produces zero, including refused speed inputs.
            // Inactive peers and other components remain byte-for-byte untouched.
            ref readonly var a=ref bodies[row];ref readonly var b=ref bodies[peer];
            double aSpeed=ratio==0?0:a.Active||a.Potential?ScaledDown(originalSpeeds[row],ratio):0;
            double bSpeed=ratio==0?0:b.Active||b.Potential?ScaledDown(originalSpeeds[peer],ratio):0;
            var d=Sub(a.Position,b.Position);
            double reach=a.Radius+b.Radius+(aSpeed+bSpeed)*Math.Max(dt,profile.CrowdLookaheadSeconds);
            return d.X*d.X+d.Z*d.Z<=reach*reach;
        }
        private static bool TryScaledDown(double speed,double ratio,out double result)
        {
            result=0;if(!Finite(speed)||speed<0||!Finite(ratio)||ratio<0||ratio>1)return false;
            if(ratio==0||speed==0)return true;
            if(ratio==1){result=speed;return true;}
            double product=speed*ratio;if(!Finite(product)||product<0)return false;
            result=Math.Max(0,NextDown(product));return Finite(result);
        }
        private static double ScaledDown(double speed,double ratio)
        {TryScaledDown(speed,ratio,out double result);return result;}
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        private static double NextDown(double value)
        {
            if(double.IsNaN(value)||value==double.NegativeInfinity)return value;if(value==0)return -double.Epsilon;
            long bits=BitConverter.DoubleToInt64Bits(value);return BitConverter.Int64BitsToDouble(bits+(value>0?-1:1));
        }
        private static double NextUp(double value)
        {
            if(double.IsNaN(value)||value==double.PositiveInfinity)return value;if(value==0)return double.Epsilon;
            long bits=BitConverter.DoubleToInt64Bits(value);return BitConverter.Int64BitsToDouble(bits+(value>0?1:-1));
        }
        private static int Root(int[] labels,int row)
        {int root=row;while(labels[root]!=root)root=labels[root];while(labels[row]!=row){int next=labels[row];labels[row]=root;row=next;}return root;}
        private static double Risk(NavMotionBody[] bodies,int row,NavPoint velocity,int[] neighbors,int count,double horizon)
        {
            double risk=0;ref readonly var a=ref bodies[row];horizon=MotorLookahead(in a,horizon);for(int k=0;k<count;k++){
                ref readonly var b=ref bodies[neighbors[row*NeighborCapacity+k]];double r=a.Radius+b.Radius;
                double gap=ClosestSquared(Sub(a.Position,b.Position),Sub(velocity,Preferred(in b)),horizon);
                risk=Math.Max(risk,Math.Max(0,1-gap/(r*r)));
            }return risk;
        }
        internal static NavMotionProposal Select(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance)
            =>Select(bodies,row,neighbors,count,profile,dt,clearance,null);
        // The crowd supplies the assigned goal before Joint. Diagnostic sampler
        // callers without an assigned goal receive no avoidance certificate.
        // Each expression is evaluated in the original scalar arithmetic order.
        // Only immutable operands move out of repeated candidate ranking; neighbor
        // traversal, ClosestSquared, relative velocity and risk reduction stay ordered.
        private struct RiskInput
        {
            internal NavPoint Separation,Preferred;
            internal double RadiusSquared;
        }
        private static double Risk(NavMotionBody[] bodies,int row,NavPoint velocity,int[] neighbors,int count,double horizon,Span<RiskInput> inputs,ref bool prepared)
        {
            // Keep out-of-contract oversized diagnostic inputs on the original path.
            if(inputs.Length!=count)return Risk(bodies,row,velocity,neighbors,count,horizon);
            if(!prepared){
                ref readonly var a=ref bodies[row];
                for(int k=0;k<count;k++){
                    ref readonly var b=ref bodies[neighbors[row*NeighborCapacity+k]];double r=a.Radius+b.Radius;
                    var separation=Sub(a.Position,b.Position);var preferred=Preferred(in b);
                    inputs[k]=new RiskInput{Separation=separation,Preferred=preferred,RadiusSquared=r*r};
                }
                prepared=true;
            }
            horizon=MotorLookahead(in bodies[row],horizon);
            double risk=0;for(int k=0;k<count;k++){
                ref readonly var input=ref inputs[k];
                double gap=ClosestSquared(input.Separation,Sub(velocity,input.Preferred),horizon);
                risk=Math.Max(risk,Math.Max(0,1-gap/input.RadiusSquared));
            }return risk;
        }
        internal static NavMotionProposal Select(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance,NavPoint? assignedGoal)
            =>Select(bodies,row,neighbors,count,profile,dt,clearance,assignedGoal,false);
        internal static NavMotionProposal Select(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance,NavPoint? assignedGoal,bool allowStaticGoalTurn,NavPoint? currentGoal=null)
        {
            // The installed endpoint owns arrival/reverse. Recast movement ranks
            // every turn and avoidance against its ONE selected current target.
            var objective=currentGoal??assignedGoal;
            bool StepAllowed(NavPoint end)=>!currentGoal.HasValue||!(clearance is NavCrowd owner)||owner.AllowsCurrentMotion(row,end);
            var a=bodies[row];a.Speed=a.OwnSpeed;var result=new NavMotionProposal{Position=a.Position,Heading=a.Heading};
            if(!a.Active||a.Speed<=0)return result;
            if(count<0){result.Overflow=true;return result;}
            bool pure=clearance is NavCrowd production&&production.StableSelectClearance;
            bool stable=pure;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            stable&=!DisableSelectClearanceReuseForQa;
#endif
            bool rankFirst=stable;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            rankFirst&=!OriginalSelectClearanceOrderForQa&&!DisableRankingBeforeClearanceForTests;
#endif
            // Exact endpoints, bounded to 3KiB of stack for the qualified
            // immutable Crossings provider. One actor/Select owns the memo;
            // eviction only repeats the original predicate. Other providers
            // retain the original 16 entries and opaque callbacks have no memo.
            int memoCapacity=clearance is NavCrowd memoOwner?memoOwner.SelectClearanceMemoCapacity:16;
#if UNITY_INCLUDE_TESTS
            if(ForceSmallClearanceMemoForTests)memoCapacity=16;
#endif
            Span<ClearanceEntry> clearEntries=stackalloc ClearanceEntry[stable?memoCapacity:0];int clearUsed=0,clearNext=0;
            // The caller holds this snapshot immutable for one Select; no cross-call cache.
            Span<RiskInput> riskInputs=stackalloc RiskInput[count<=NeighborCapacity?count:0];bool riskPrepared=false;
            var delta=Sub(a.Target,a.Position);double distance=Length(delta),aim=Math.Atan2(delta.Z,delta.X),limit=a.TurnSpeed*dt;
            // Reverse is limited to the assigned FINAL point. Intermediate route corners
            // retain forward steering and all route/terrain guards. Exact joint sweeps
            // below still arbitrate peers; this proposal carries no avoidance certificate.
            if(a.ReverseDistance>0&&assignedGoal.HasValue&&a.Target.Equals(assignedGoal.Value)&&distance<=a.ReverseDistance&&
                Math.Abs(Angle(aim-a.Heading-Math.PI))<=a.ReverseConeRadians){
                double rearAim=Angle(aim+Math.PI),rearHeading=Turn(a.Heading,rearAim,limit);
                bool aligned=Math.Abs(Angle(rearAim-rearHeading))<=profile.ForwardAlignmentAngle;
                double speed=Math.Min(a.Speed*a.ReverseSpeedRatio,distance/dt);
                var eventual=Velocity(rearAim,-speed);var velocity=aligned?Velocity(rearHeading,-speed):default;
                var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                var future=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                if(SelectClear(row,end,clearance,clearEntries,ref clearUsed,ref clearNext)&&SelectClear(row,future,clearance,clearEntries,ref clearUsed,ref clearNext)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt)&&
                    Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared)<=Risk(bodies,row,default,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared)+1e-12)
                    return new NavMotionProposal{Position=end,Velocity=velocity,Heading=rearHeading,IndependentTurn=!aligned&&!rearHeading.Equals(a.Heading)&&ImprovesGoal(a.Position,future,assignedGoal.Value)};
            }
            double turning=Turn(a.Heading,aim,limit);result.Heading=turning;
            var preferred=Preferred(in a);double bestRisk=Risk(bodies,row,default,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared),bestError=a.Speed*a.Speed;
            // Squared-velocity errors need a scale-relative comparison at tiny
            // composed speeds; retain the existing tolerance at normal speeds.
            double errorEpsilon=Math.Min(1e-12,a.Speed*a.Speed*1e-12);
            bool staticTurnThreat=objective.HasValue&&OnlyStaticPreferredThreat(bodies,row,neighbors,count,preferred,profile.CrowdLookaheadSeconds);
            bool futureStaticMove=false,won=false,roleCapWait=false;double bestSteering=aim,bestSpeed=0;
            bool hasStaticGoalEscape=false;double escapeError=double.PositiveInfinity;NavMotionProposal staticGoalEscape=default;
            // With no explicit current motor, this opt-in escape is owned by
            // the assigned endpoint. Ranking it against an opposing temporary
            // target can certify a turn that ultimately drives away from goal.
            // Recast retries supply currentGoal and retain their current intent,
            // including necessary avoidance legs initially away from that motor.
            bool endpointEscape=allowStaticGoalTurn&&!currentGoal.HasValue&&assignedGoal.HasValue;
            var escapePreferred=preferred;
            if(endpointEscape){var escapeBody=a;escapeBody.Target=assignedGoal.Value;escapePreferred=Preferred(in escapeBody);}
            // Steering aims include an explicit turn-in-place path to an
            // avoidance direction. Its eventual velocity is only a heuristic;
            // the proposal still executes bounded turn, then aligned forward.
            // Rank the normal choices first. If prediction alone prefers a
            // wait or non-improving retreat, rank the same reachable improving choices
            // without treating wait's forecast as a physical safety veto.
            // Full-dt terrain and joint sweeps below remain authoritative.
            for(int pass=0;pass<2;pass++){
            if(pass==1){if(won&&bestError<a.Speed*a.Speed-errorEpsilon)break;bestRisk=double.PositiveInfinity;bestError=a.Speed*a.Speed;}
            for(int direction=-2;direction<profile.CrowdHeadingIntervals;direction++){
                double steering=direction==-2?a.Heading:direction==-1?aim:Angle(aim-Math.PI+2*Math.PI*direction/profile.CrowdHeadingIntervals);
                double heading=Turn(a.Heading,steering,limit);
                bool aligned=Math.Abs(Angle(steering-heading))<=profile.ForwardAlignmentAngle;
                for(int band=1;band<=profile.CrowdSpeedBands;band++){
                    double speed=Math.Min(a.Speed,distance/dt)*band/profile.CrowdSpeedBands;
                    var eventual=Velocity(steering,speed);var velocity=aligned?Velocity(heading,speed):default;
                    var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                    var futureEnd=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                    // A physically necessary avoidance leg may initially move away
                    // from the current target. Rank the same bounded zero-risk
                    // choices by that target's preferred velocity, not by a second
                    // final endpoint. Full forecast/dt safety still qualifies it;
                    // it replaces only an exhausted normal winner on stalled retry.
                    double escapeEx=eventual.X-escapePreferred.X,escapeEz=eventual.Z-escapePreferred.Z;
                    double candidateEscapeError=escapeEx*escapeEx+escapeEz*escapeEz;
                    if(allowStaticGoalTurn&&staticTurnThreat&&(aligned||!heading.Equals(a.Heading))&&objective.HasValue&&(!assignedGoal.HasValue||!a.Position.Equals(assignedGoal.Value))&&candidateEscapeError<escapeError-errorEpsilon&&
                        (!endpointEscape||ImprovesGoal(a.Position,futureEnd,assignedGoal.Value)&&(end.Equals(a.Position)||ImprovesGoal(a.Position,end,assignedGoal.Value)))&&StepAllowed(end)&&StepAllowed(futureEnd)&&
                        Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared)<=1e-12&&
                        StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt)&&
                        SelectClear(row,end,clearance,clearEntries,ref clearUsed,ref clearNext)&&SelectClear(row,futureEnd,clearance,clearEntries,ref clearUsed,ref clearNext)&&
                        SelectClear(row,StaticTurnForecastEnd(a.Position,steering,speed,distance,Math.Max(dt,profile.CrowdLookaheadSeconds)),clearance,clearEntries,ref clearUsed,ref clearNext)){
                        staticGoalEscape=new NavMotionProposal{Position=end,Velocity=velocity,Heading=heading,IndependentTurn=!aligned};hasStaticGoalEscape=true;escapeError=candidateEscapeError;
                    }
                    // Once an eventual translation is already known available,
                    // a losing candidate cannot change Blocked or the winner.
                    // Only audited pure clearance may omit these queries. Until
                    // that witness exists, retain the original query order too.
                    bool ranked=rankFirst&&futureStaticMove;double risk=0,error=0;
                    if(ranked){
                        risk=Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared);
                        double ex=eventual.X-preferred.X,ez=eventual.Z-preferred.Z;error=ex*ex+ez*ez;
                        if(pass==1&&error>=a.Speed*a.Speed-errorEpsilon)continue;
                        if(risk>bestRisk+1e-12||Math.Abs(risk-bestRisk)<=1e-12&&error>=bestError-errorEpsilon)continue;
                    }
                    bool futureAvailable=false;
                    if(!aligned||!futureStaticMove)futureAvailable=SelectClear(row,futureEnd,clearance,clearEntries,ref clearUsed,ref clearNext)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt);
                    if(futureAvailable)futureStaticMove=true;
                    if(!aligned&&!futureAvailable)continue;
                    if(!StepAllowed(end)||!StepAllowed(futureEnd)){
                        // Preserve the reason lost by screening before selection.
                        // A cap denial alone is insufficient: the same improving
                        // choice must pass physical, peer and ranking predicates.
                        if(!roleCapWait&&pure&&clearance is NavCrowd capOwner&&capOwner.AllowsCurrentMotionWithoutRowCap(row,end,futureEnd)){
                            double deniedRisk=ranked?risk:Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared);
                            double dx=eventual.X-preferred.X,dz=eventual.Z-preferred.Z,deniedError=ranked?error:dx*dx+dz*dz;
                            if(deniedError<a.Speed*a.Speed-errorEpsilon&&deniedRisk<=bestRisk+1e-12&&
                                (Math.Abs(deniedRisk-bestRisk)>1e-12||deniedError<bestError-errorEpsilon)&&
                                SelectClear(row,end,clearance,clearEntries,ref clearUsed,ref clearNext)&&SelectClear(row,futureEnd,clearance,clearEntries,ref clearUsed,ref clearNext)&&
                                StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt)&&
                                (!staticTurnThreat||aligned||heading.Equals(a.Heading)||!objective.HasValue||!ImprovesGoal(a.Position,futureEnd,objective.Value)||
                                    SelectClear(row,StaticTurnForecastEnd(a.Position,steering,speed,distance,Math.Max(dt,profile.CrowdLookaheadSeconds)),clearance,clearEntries,ref clearUsed,ref clearNext)))roleCapWait=true;
                        }
                        continue;
                    }
                    if(!SelectClear(row,end,clearance,clearEntries,ref clearUsed,ref clearNext))continue;
                    // The executed heading may differ from the forecast steering.
                    // A known stationary footprint must clear the actual dt sweep;
                    // otherwise the same ranked candidate is retracted by Joint forever.
                    if(!StaticPeersClear(bodies,row,neighbors,count,velocity,dt))continue;
                    if(!ranked){
                        risk=Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared);
                        double ex=eventual.X-preferred.X,ez=eventual.Z-preferred.Z;error=ex*ex+ez*ez;
                    }
                    if(pass==1&&error>=a.Speed*a.Speed-errorEpsilon)continue;
                    // Stable clockwise enumeration breaks an exact reciprocal tie.
                    if(risk>bestRisk+1e-12||Math.Abs(risk-bestRisk)<=1e-12&&error>=bestError-errorEpsilon)continue;
                    // A reachable dt turn may still aim into a physical dead end.
                    // This qualifies only selection; futureStaticMove above remains
                    // the original dt witness for Blocked/local-repair semantics.
                    bool voluntaryTurn=objective.HasValue&&!aligned&&futureAvailable&&velocity.Equals(default(NavPoint))&&!heading.Equals(a.Heading)&&ImprovesGoal(a.Position,futureEnd,objective.Value);
                    if(staticTurnThreat&&voluntaryTurn){
                        var forecast=StaticTurnForecastEnd(a.Position,steering,speed,distance,Math.Max(dt,profile.CrowdLookaheadSeconds));
                        if(!SelectClear(row,forecast,clearance,clearEntries,ref clearUsed,ref clearNext))continue;
                    }
                    bestRisk=risk;bestError=error;bestSteering=steering;bestSpeed=speed;result.Position=end;result.Velocity=velocity;result.Heading=heading;won=true;
                    // Set on EVERY winner replacement, using this winner's own
                    // futureAvailable, not the accumulated futureStaticMove.
                    result.IndependentTurn=voluntaryTurn;
                }
            }
            }
            // A turn is a voluntary wait only when some eventual translated
            // endpoint is physically available. Enclosure keeps the old timeout.
            if(won&&result.IndependentTurn&&staticTurnThreat){
                bool reuseRefinement=stable;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                reuseRefinement&=!DisableRefinementReuseForQa;
#endif
                if(reuseRefinement)RefineStaticAvoidanceTurn(bodies,row,neighbors,count,profile,dt,clearance,objective,aim,limit,bestSteering,bestSpeed,bestRisk,bestError,errorEpsilon,ref result,clearEntries,ref clearUsed,ref clearNext,riskInputs,ref riskPrepared,currentGoal.HasValue);
                else RefineStaticAvoidanceTurn(bodies,row,neighbors,count,profile,dt,clearance,objective,aim,limit,bestSteering,bestSpeed,bestRisk,bestError,errorEpsilon,ref result,currentGoal.HasValue);
            }
            if(!won&&hasStaticGoalEscape){result=staticGoalEscape;won=true;futureStaticMove=true;if(CausalActor!=0&&a.Id==CausalActor)Causal.RetryGoalTurn=result.IndependentTurn;}
            result.Blocked=!futureStaticMove;
            // An active neighbor is a forecast input, not evidence that this
            // stationary no-winner will recover. Keep actual turns voluntary;
            // an aligned exhausted choice must reach the existing bounded detour.
            result.StaticNoWinner=!won&&turning.Equals(a.Heading)&&!result.Blocked;
            result.RoleCapWait=!won&&roleCapWait;
            // A reachable retreat is not a certificate that a stationary turn
            // will advance the ONE current executable objective. If all such
            // controls were exhausted, expose that outcome to existing bounded
            // recovery. A qualified role-cap wait and useful avoidance turn keep
            // their original treatment; no timer or progress credit changes.
            result.CurrentProgressExhausted=currentGoal.HasValue&&!roleCapWait&&result.Position.Equals(a.Position)&&!result.IndependentTurn&&bestError>=a.Speed*a.Speed-errorEpsilon;
            return result;
        }
#if UNITY_INCLUDE_TESTS
        // Original scalar ranking with the same static-threat turn correction.
        // Independent control for risk-input reuse and ordered clearance calls.
        internal static NavMotionProposal SelectRiskPredecessorForQa(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance,NavPoint? assignedGoal)
        {
            var a=bodies[row];a.Speed=a.OwnSpeed;var result=new NavMotionProposal{Position=a.Position,Heading=a.Heading};
            if(!a.Active||a.Speed<=0)return result;
            if(count<0){result.Overflow=true;return result;}
            var delta=Sub(a.Target,a.Position);double distance=Length(delta),aim=Math.Atan2(delta.Z,delta.X),limit=a.TurnSpeed*dt;
            // Reverse is limited to the assigned FINAL point. Intermediate route corners
            // retain forward steering and all route/terrain guards. Exact joint sweeps
            // below still arbitrate peers; this proposal carries no avoidance certificate.
            if(a.ReverseDistance>0&&assignedGoal.HasValue&&a.Target.Equals(assignedGoal.Value)&&distance<=a.ReverseDistance&&
                Math.Abs(Angle(aim-a.Heading-Math.PI))<=a.ReverseConeRadians){
                double rearAim=Angle(aim+Math.PI),rearHeading=Turn(a.Heading,rearAim,limit);
                bool aligned=Math.Abs(Angle(rearAim-rearHeading))<=profile.ForwardAlignmentAngle;
                double speed=Math.Min(a.Speed*a.ReverseSpeedRatio,distance/dt);
                var eventual=Velocity(rearAim,-speed);var velocity=aligned?Velocity(rearHeading,-speed):default;
                var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                var future=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                if(clearance.ClearMotion(row,end)&&clearance.ClearMotion(row,future)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt)&&
                    Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds)<=Risk(bodies,row,default,neighbors,count,profile.CrowdLookaheadSeconds)+1e-12)
                    return new NavMotionProposal{Position=end,Velocity=velocity,Heading=rearHeading,IndependentTurn=!aligned&&!rearHeading.Equals(a.Heading)&&ImprovesGoal(a.Position,future,assignedGoal.Value)};
            }
            double turning=Turn(a.Heading,aim,limit);result.Heading=turning;
            var preferred=Preferred(in a);double bestRisk=Risk(bodies,row,default,neighbors,count,profile.CrowdLookaheadSeconds),bestError=a.Speed*a.Speed;
            // Squared-velocity errors need a scale-relative comparison at tiny
            // composed speeds; retain the existing tolerance at normal speeds.
            double errorEpsilon=Math.Min(1e-12,a.Speed*a.Speed*1e-12);
            bool staticTurnThreat=assignedGoal.HasValue&&OnlyStaticPreferredThreat(bodies,row,neighbors,count,preferred,profile.CrowdLookaheadSeconds);
            bool futureStaticMove=false,won=false;double bestSteering=aim,bestSpeed=0;
            // Steering aims include an explicit turn-in-place path to an
            // avoidance direction. Its eventual velocity is only a heuristic;
            // the proposal still executes bounded turn, then aligned forward.
            // Rank the normal choices first. If prediction alone prefers a
            // wait or non-improving retreat, rank the same reachable improving choices
            // without treating wait's forecast as a physical safety veto.
            // Full-dt terrain and joint sweeps below remain authoritative.
            for(int pass=0;pass<2;pass++){
            if(pass==1){if(won&&bestError<a.Speed*a.Speed-errorEpsilon)break;bestRisk=double.PositiveInfinity;bestError=a.Speed*a.Speed;}
            for(int direction=-2;direction<profile.CrowdHeadingIntervals;direction++){
                double steering=direction==-2?a.Heading:direction==-1?aim:Angle(aim-Math.PI+2*Math.PI*direction/profile.CrowdHeadingIntervals);
                double heading=Turn(a.Heading,steering,limit);
                bool aligned=Math.Abs(Angle(steering-heading))<=profile.ForwardAlignmentAngle;
                for(int band=1;band<=profile.CrowdSpeedBands;band++){
                    double speed=Math.Min(a.Speed,distance/dt)*band/profile.CrowdSpeedBands;
                    var eventual=Velocity(steering,speed);var velocity=aligned?Velocity(heading,speed):default;
                    var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                    var futureEnd=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                    bool futureAvailable=false;
                    if(!aligned||!futureStaticMove)futureAvailable=clearance.ClearMotion(row,futureEnd)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt);
                    if(futureAvailable)futureStaticMove=true;
                    if(!aligned&&!futureAvailable)continue;
                    if(!clearance.ClearMotion(row,end))continue;
                    if(!StaticPeersClear(bodies,row,neighbors,count,velocity,dt))continue;
                    double risk=Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds);
                    double ex=eventual.X-preferred.X,ez=eventual.Z-preferred.Z,error=ex*ex+ez*ez;
                    if(pass==1&&error>=a.Speed*a.Speed-errorEpsilon)continue;
                    // Stable clockwise enumeration breaks an exact reciprocal tie.
                    if(risk>bestRisk+1e-12||Math.Abs(risk-bestRisk)<=1e-12&&error>=bestError-errorEpsilon)continue;
                    bool voluntaryTurn=assignedGoal.HasValue&&!aligned&&futureAvailable&&velocity.Equals(default(NavPoint))&&!heading.Equals(a.Heading)&&ImprovesGoal(a.Position,futureEnd,assignedGoal.Value);
                    if(staticTurnThreat&&voluntaryTurn&&!clearance.ClearMotion(row,StaticTurnForecastEnd(a.Position,steering,speed,distance,Math.Max(dt,profile.CrowdLookaheadSeconds))))continue;
                    bestRisk=risk;bestError=error;bestSteering=steering;bestSpeed=speed;result.Position=end;result.Velocity=velocity;result.Heading=heading;won=true;
                    // Set on EVERY winner replacement, using this winner's own
                    // futureAvailable, not the accumulated futureStaticMove.
                    result.IndependentTurn=voluntaryTurn;
                }
            }
            }
            // A turn is a voluntary wait only when some eventual translated
            // endpoint is physically available. Enclosure keeps the old timeout.
            if(won&&result.IndependentTurn&&staticTurnThreat)
                RefineStaticAvoidanceTurn(bodies,row,neighbors,count,profile,dt,clearance,assignedGoal,aim,limit,bestSteering,bestSpeed,bestRisk,bestError,errorEpsilon,ref result);
            result.Blocked=!futureStaticMove;
            // An active neighbor is a forecast input, not evidence that this
            // stationary no-winner will recover. Keep actual turns voluntary;
            // an aligned exhausted choice must reach the existing bounded detour.
            result.StaticNoWinner=!won&&turning.Equals(a.Heading)&&!result.Blocked;
            return result;
        }
#endif
        internal static bool ImprovesGoal(NavPoint from,NavPoint to,NavPoint goal)
        {
            if(!Finite(from.X)||!Finite(from.Z)||!Finite(to.X)||!Finite(to.Z)||!Finite(goal.X)||!Finite(goal.Z))return false;
            double bx=goal.X-from.X,bz=goal.Z-from.Z,ax=goal.X-to.X,az=goal.Z-to.Z,before=bx*bx+bz*bz,after=ax*ax+az*az;
            return Finite(before)&&Finite(after)&&after<before;
        }
        private static NavPoint StaticTurnForecastEnd(NavPoint position,double steering,double speed,double distance,double horizon)
        {var reach=Velocity(steering,Math.Min(distance,speed*horizon));return new NavPoint(position.X+reach.X,position.Z+reach.Z);}
        private static double MotorLookahead(in NavMotionBody body,double horizon)
            =>!body.Yielding&&body.LocalLookaheadSeconds>0?Math.Min(horizon,body.LocalLookaheadSeconds):horizon;
        private static bool OnlyStaticPreferredThreat(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavPoint preferred,double horizon)
        {
            bool staticThreat=false;ref readonly var a=ref bodies[row];horizon=MotorLookahead(in a,horizon);
            for(int k=0;k<count;k++){
                ref readonly var b=ref bodies[neighbors[row*NeighborCapacity+k]];double radius=a.Radius+b.Radius;
                double risk=Math.Max(0,1-ClosestSquared(Sub(a.Position,b.Position),Sub(preferred,Preferred(in b)),horizon)/(radius*radius));
                if(risk<=1e-12)continue;
                // Moving peers retain the sampled reciprocal policy. A static
                // obstacle is the only cause this correction is allowed to refine.
                if(b.Active)return false;staticThreat=true;
            }
            return staticThreat;
        }
        private static void RefineStaticAvoidanceTurn(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance,NavPoint? assignedGoal,double aim,double limit,double steering,double speed,double risk,double error,double errorEpsilon,ref NavMotionProposal result,bool currentIntent=false)
        {
            // Independent predecessor and arbitrary callbacks keep every original
            // query. Empty spans make both reuse helpers follow their scalar path.
            int used=0,next=0;bool prepared=false;
            RefineStaticAvoidanceTurn(bodies,row,neighbors,count,profile,dt,clearance,assignedGoal,aim,limit,steering,speed,risk,error,errorEpsilon,ref result,default,ref used,ref next,default,ref prepared,currentIntent);
        }
        private static void RefineStaticAvoidanceTurn(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance,NavPoint? assignedGoal,double aim,double limit,double steering,double speed,double risk,double error,double errorEpsilon,ref NavMotionProposal result,Span<ClearanceEntry> clearEntries,ref int clearUsed,ref int clearNext,Span<RiskInput> riskInputs,ref bool riskPrepared,bool currentIntent=false)
        {
            if(risk>1e-12||error<=errorEpsilon)return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(CountRefinementForQa){RefinementCallsForQa++;if(clearEntries.Length>0)RefinementReuseCallsForQa++;}
#endif
            // Only the audited production capability shares the caller's existing
            // stack spans. Bodies/clearance stay immutable for this one Select.
            var a=bodies[row];double safe=Angle(steering-aim),unsafeAngle=0;
            // Sampling may choose a distant safe direction on one tick and the
            // desired direction on the next, repeatedly reversing a turn in place.
            // Refine the existing safe angular component toward the desired aim.
            // 64 bounds binary64 angular precision; it is not a gameplay setting.
            for(int bit=0;bit<64;bit++){
                double angularGap=safe-unsafeAngle;if(angularGap*angularGap*speed*speed<=errorEpsilon)break;
                double middle=safe+(unsafeAngle-safe)*.5;if(middle==safe||middle==unsafeAngle)break;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(CountRefinementForQa)RefinementIterationsForQa++;
#endif
                double candidate=Angle(aim+middle),heading=Turn(a.Heading,candidate,limit);
                bool aligned=Math.Abs(Angle(candidate-heading))<=profile.ForwardAlignmentAngle;
                var eventual=Velocity(candidate,speed);var velocity=aligned?Velocity(heading,speed):default;
                var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                var future=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                bool available=(!currentIntent||!(clearance is NavCrowd owner)||owner.AllowsCurrentMotion(row,end)&&owner.AllowsCurrentMotion(row,future))&&SelectClear(row,end,clearance,clearEntries,ref clearUsed,ref clearNext)&&SelectClear(row,future,clearance,clearEntries,ref clearUsed,ref clearNext)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt)&&StaticPeersClear(bodies,row,neighbors,count,velocity,dt);
                bool voluntaryTurn=assignedGoal.HasValue&&!aligned&&velocity.Equals(default(NavPoint))&&!heading.Equals(a.Heading)&&ImprovesGoal(a.Position,future,assignedGoal.Value);
                if(available&&voluntaryTurn)available=SelectClear(row,StaticTurnForecastEnd(a.Position,candidate,speed,Length(Sub(a.Target,a.Position)),Math.Max(dt,profile.CrowdLookaheadSeconds)),clearance,clearEntries,ref clearUsed,ref clearNext);
                if(available){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if(CountRefinementForQa&&riskInputs.Length==count)RefinementRiskReuseCallsForQa++;
#endif
                    available=Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds,riskInputs,ref riskPrepared)<=risk+1e-12;
                }
                if(available){
                    safe=middle;result.Position=end;result.Velocity=velocity;result.Heading=heading;
                    result.IndependentTurn=voluntaryTurn;
                }else unsafeAngle=middle;
            }
        }
        private static bool StaticPeersClear(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavPoint velocity,double dt)
        {
            ref readonly var a=ref bodies[row];for(int k=0;k<count;k++){ref readonly var b=ref bodies[neighbors[row*NeighborCapacity+k]];if(b.Active)continue;double r=a.Radius+b.Radius;if(ClosestSquared(Sub(a.Position,b.Position),velocity,dt)<r*r-1e-9)return false;}return true;
        }
        private static void RefuseJoint(int actor,int peer,NavMotionBody[] bodies,NavMotionProposal[] proposals,double dt)
        {
            ObserveCausalConflict(actor,peer,bodies,proposals,dt);
            // Record only an executed candidate's real sweep conflict. The
            // first cause survives subsequent fixed-point/owner passes of this
            // frame; no contact guess or persistent dependency graph is added.
            if(!proposals[actor].HasJointRefuser){proposals[actor].HasJointRefuser=true;proposals[actor].JointFirstRefuser=bodies[peer].Id;}
            proposals[actor].Refuse=true;
        }
        internal static void ResolveJoint(NavMotionBody[] bodies,NavMotionProposal[] proposals,double dt)
        {
            // Keep actual rejected translation as frame provenance. A forecast
            // wait or a turn alone still supplies no physical refusal evidence.
            // Refusal changes a moving hazard into a stationary one. At most N
            // trajectories can be refused, so the fixed point is finite.
            if(CausalActor!=0)Causal.JointPasses++;
            bool changed;do{changed=false;for(int i=0;i<bodies.Length;i++){
                // Velocities change only in the final refusal sweep, not while
                // pairs are inspected. Reuse this row's frozen proposal.
                var vi=proposals[i].Velocity;bool stationaryI=vi.X==0&&vi.Z==0;
                ref readonly var a=ref bodies[i];
                for(int j=i+1;j<bodies.Length;j++){
                    var vj=proposals[j].Velocity;
                    // Two exact stationary proposals cannot acquire a refusal here,
                    // even for overlapping footprints. Keep the final Refuse sweep.
                    if(stationaryI&&vj.X==0&&vj.Z==0)continue;
                    double r=a.Radius+bodies[j].Radius;
                    var separation=Sub(a.Position,bodies[j].Position);
                    if(ClosestSquared(separation,Sub(vi,vj),dt)>=r*r-1e-9)continue;
                    bool movingI=Length(vi)>0,movingJ=Length(vj)>0;
                    if(movingI&&movingJ){
                        bool aloneI=ClosestSquared(separation,vi,dt)>=r*r-1e-9,aloneJ=ClosestSquared(separation,Sub(default,vj),dt)>=r*r-1e-9;
                        // Keep an already terrain-validated proposal only when the
                        // other footprint can wait safely. Stable entity identity
                        // resolves a reciprocal tie, never the frame row order.
                        if(aloneI&&(!aloneJ||bodies[i].Id<bodies[j].Id)){RefuseJoint(j,i,bodies,proposals,dt);continue;}
                        if(aloneJ){RefuseJoint(i,j,bodies,proposals,dt);continue;}
                    }
                    if(movingI){RefuseJoint(i,j,bodies,proposals,dt);}
                    if(movingJ){RefuseJoint(j,i,bodies,proposals,dt);}
                }
            }
            for(int i=0;i<bodies.Length;i++)if(proposals[i].Refuse){proposals[i].Refuse=false;if(!proposals[i].Velocity.Equals(default(NavPoint))&&!proposals[i].Position.Equals(bodies[i].Position))proposals[i].JointRefused=true;proposals[i].Position=bodies[i].Position;proposals[i].Velocity=default;changed=true;}
            }while(changed);
        }
    }
}
