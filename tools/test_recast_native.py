"""Exercise the production C ABI on independent tiny geometry (no Unity)."""
from contextlib import ExitStack
import ctypes as c
import pathlib
import unittest
import threading

ROOT = pathlib.Path(__file__).resolve().parents[1]

CANCELLED=c.CFUNCTYPE(c.c_int)
ADMIT=c.CFUNCTYPE(c.c_void_p,c.c_uint64)
RELEASE=c.CFUNCTYPE(None,c.c_void_p)

class RecastNativeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib = c.CDLL(str(ROOT / '.local/recast-native/libspacewars_recast.dylib'))
        l = cls.lib
        l.sw_recast_memory_scope.argtypes=[ADMIT,RELEASE]
        l.sw_recast_memory_abi.restype=c.c_int
        l.sw_recast_allocation_count.restype=c.c_uint64
        l.sw_recast_allocation_bytes.restype=c.c_uint64
        l.sw_recast_build.restype = c.c_void_p
        l.sw_recast_build.argtypes = [c.POINTER(c.c_float),c.c_int,c.POINTER(c.c_int),c.POINTER(c.c_uint8),c.c_int,c.POINTER(c.c_uint8)]+[c.c_float]*6+[CANCELLED]
        l.sw_recast_build_reusing_tiles.restype=c.c_void_p
        l.sw_recast_build_reusing_tiles.argtypes=l.sw_recast_build.argtypes+[c.c_void_p]
        l.sw_recast_reused_tiles.argtypes=[c.c_void_p]
        l.sw_recast_map_free.argtypes = [c.c_void_p]
        l.sw_recast_query.restype = c.c_void_p
        l.sw_recast_query.argtypes = [c.c_void_p,c.POINTER(c.c_float),c.POINTER(c.c_float),c.c_int,c.c_int,c.c_int]
        l.sw_recast_connectivity_work.argtypes=[c.c_void_p]
        l.sw_recast_connectivity_work.restype=c.c_uint64
        l.sw_recast_query_free.argtypes = [c.c_void_p]
        l.sw_recast_step.argtypes = [c.c_void_p,c.c_int,c.POINTER(c.c_int)]
        l.sw_recast_copy_areas.argtypes=[c.c_void_p,c.POINTER(c.c_uint8),c.c_int,c.POINTER(c.c_int)]
        l.sw_recast_copy.argtypes = [c.c_void_p,c.POINTER(c.c_float),c.c_int,c.POINTER(c.c_int)]

    def test_campaign_width_keeps_local_raster_resolution(self):
        vertices = (c.c_float * 12)(-240, 0, -240, 240, 0, -240, 240, 0, 240, -240, 0, 240)
        triangles = (c.c_int * 6)(0, 2, 1, 0, 3, 2)
        areas = (c.c_uint8 * 2)(1, 1)
        allowed = (c.c_uint8 * 4096)()
        allowed[65] = 1
        mesh = self.lib.sw_recast_build(
            vertices, 4, triangles, areas, 2, allowed, .2, .05, .72, .1, .1, 89, CANCELLED())
        self.assertTrue(mesh, '480 m of support at 0.2 m per cell needs 38 by 38 tiles')
        try:
            status = self.route(mesh, (200, 0, 220), (230, 0, 220), 1, 1)
            self.assertEqual(1, status)
        finally:
            self.lib.sw_recast_map_free(mesh)

    def test_layered_explicit_links_preserve_overlapping_floors_direction_and_provenance(self):
        l=self.lib
        l.sw_recast_build_layered.restype=c.c_void_p
        l.sw_recast_build_layered.argtypes=l.sw_recast_build.argtypes+[c.POINTER(c.c_float),c.POINTER(c.c_uint8),c.POINTER(c.c_uint8),c.POINTER(c.c_uint32),c.c_int]
        l.sw_recast_copy_portals.argtypes=[c.c_void_p,c.POINTER(c.c_uint32),c.c_int,c.POINTER(c.c_int)]
        verts=(c.c_float*24)(-8,0,-6,8,0,-6,8,0,6,-8,0,6,-8,4,-6,8,4,-6,8,4,6,-8,4,6)
        tris=(c.c_int*12)(0,2,1,0,3,2,4,6,5,4,7,6)
        areas=(c.c_uint8*4)(1,1,2,2); allowed=(c.c_uint8*4096)();allowed[65]=allowed[130]=1
        links=(c.c_float*6)(-2,0,0,2,4,0);la=(c.c_uint8*2)(1,2);ids=(c.c_uint32*1)(73)
        baseline=l.sw_recast_allocation_count()
        def run(both,count,reverse):
            m=l.sw_recast_build_layered(verts,8,tris,areas,4,allowed,.25,.0625,.25,.125,.125,89,CANCELLED(),links,la,(c.c_uint8*1)(both),ids,count)
            self.assertTrue(m)
            q=None
            try:
                start=(c.c_float*3)(-5,0,0);end=(c.c_float*3)(5,4,0)
                q=l.sw_recast_query(m,end if reverse else start,start if reverse else end,2 if reverse else 1,1 if reverse else 2,4096);self.assertTrue(q)
                used=c.c_int();state=0
                for _ in range(100):
                    state=l.sw_recast_step(q,64,c.byref(used))
                    if state:break
                if count and (not reverse or both):
                    self.assertEqual(state,1)
                    out=(c.c_uint32*4096)();n=c.c_int();self.assertEqual(l.sw_recast_copy_portals(q,out,4096,c.byref(n)),1);self.assertIn(73,list(out)[:n.value])
                    semantic=(c.c_uint8*4096)();self.assertEqual(l.sw_recast_copy_areas(q,semantic,4096,c.byref(n)),1);self.assertEqual(semantic[0],2 if reverse else 1);self.assertEqual(semantic[n.value-1],1 if reverse else 2)
                else:self.assertEqual(state,3)
            finally:
                if q:l.sw_recast_query_free(q)
                l.sw_recast_map_free(m)
        run(0,1,False);run(0,1,True);run(1,1,True);run(1,0,False)
        self.assertEqual(l.sw_recast_allocation_count(),baseline)

    class Owner:
        def __init__(self, deny=0):
            self.calls=0;self.live={};self.peak=0;self.errors=[];self.deny=deny
            def admit(size):
                self.calls+=1
                if self.calls==self.deny:return None
                self.live[self.calls]=size
                self.peak=max(self.peak,sum(self.live.values()))
                return self.calls
            def release(token):
                if token not in self.live:self.errors.append(token)
                else:del self.live[token]
            self.admit=ADMIT(admit);self.release=RELEASE(release)
        def scope(self, lib):
            assert lib.sw_recast_memory_scope(self.admit,self.release)==1
        @staticmethod
        def clear(lib):assert lib.sw_recast_memory_scope(ADMIT(),RELEASE())==1

    def mesh(self, reverse=True, cancelled=None):
        # Two touching flat semantic regions. No render geometry or float-floor guessing.
        vertices = (c.c_float*24)(-8,0,-6, 0,0,-6, 0,0,6, -8,0,6, 0,0,-6, 8,0,-6, 8,0,6, 0,0,6)
        triangles = (c.c_int*12)(0,2,1,0,3,2,4,6,5,4,7,6)
        areas = (c.c_uint8*4)(1,1,2,2)
        allowed = (c.c_uint8*4096)()
        allowed[65]=allowed[130]=allowed[66]=1
        allowed[129]=int(reverse)
        m = self.lib.sw_recast_build(vertices,8,triangles,areas,4,allowed,.25,.05,.5,1,.2,45,CANCELLED(cancelled or (lambda: 0)))
        if cancelled is not None:
            self.assertFalse(m)
            return m
        self.assertTrue(m)
        self.addCleanup(self.lib.sw_recast_map_free,m)
        return m

    def route(self,m,start,goal,sa,ga,details=False):
        q=self.lib.sw_recast_query(m,(c.c_float*3)(*start),(c.c_float*3)(*goal),sa,ga,1024)
        self.assertTrue(q)
        try:
            consumed=c.c_int()
            for _ in range(1024):
                state=self.lib.sw_recast_step(q,1,c.byref(consumed))
                self.assertLessEqual(consumed.value,1)
                if state: break
            self.assertNotEqual(state,0,'finite sliced query did not terminate')
            transcript=None
            if state==1:
                count=c.c_int();points=(c.c_float*(4096*3))()
                self.assertEqual(self.lib.sw_recast_copy(q,points,4096,c.byref(count)),1)
                self.assertGreaterEqual(count.value,2)
                semantic=(c.c_uint8*count.value)();semantic_count=c.c_int()
                self.assertEqual(self.lib.sw_recast_copy_areas(q,semantic,count.value,c.byref(semantic_count)),1)
                self.assertEqual(semantic_count.value,count.value)
                self.assertEqual(semantic[0],sa)
                self.assertEqual(semantic[count.value-1],ga)
                self.assertAlmostEqual(points[(count.value-1)*3],goal[0],places=3)
                if details:
                    transcript=(tuple(points[:count.value*3]),bytes(semantic))
            return (state,transcript) if details else state
        finally: self.lib.sw_recast_query_free(q)

    def test_active_query_restores_exact_frontier_and_next_steps(self):
        lib = self.lib
        word_pointer = c.POINTER(c.c_uint32)
        lib.sw_recast_capture_query.argtypes = [c.c_void_p, word_pointer, c.c_int]
        lib.sw_recast_restore_query.argtypes = [c.c_void_p, word_pointer, c.c_int]
        mesh = self.mesh()

        with ExitStack() as handles:
            def new_query():
                query = lib.sw_recast_query(mesh, (c.c_float*3)(-5, 0, 0), (c.c_float*3)(5, 0, 0), 1, 2, 1024)
                self.assertTrue(query)
                handles.callback(lib.sw_recast_query_free, query)
                return query

            def capture(query):
                count = lib.sw_recast_capture_query(query, None, 0)
                self.assertGreater(count, 0)
                buffer = (c.c_uint32 * count)()
                self.assertEqual(count, lib.sw_recast_capture_query(query, buffer, count))
                return buffer

            original = new_query()
            consumed = c.c_int()
            self.assertEqual(0, lib.sw_recast_step(original, 1, c.byref(consumed)))
            self.assertEqual(1, consumed.value)
            saved = capture(original)
            restored = new_query()
            self.assertEqual(1, lib.sw_recast_restore_query(restored, saved, len(saved)))
            self.assertEqual(bytes(saved), bytes(capture(restored)))
            for _ in range(1024):
                expected = lib.sw_recast_step(original, 1, c.byref(consumed))
                actual_count = c.c_int()
                self.assertEqual(expected, lib.sw_recast_step(restored, 1, c.byref(actual_count)))
                self.assertEqual(consumed.value, actual_count.value)
                if expected:
                    self.assertEqual(1, expected)
                    break
                self.assertEqual(bytes(capture(original)), bytes(capture(restored)))
            else:
                self.fail("sliced query did not finish")
            paths = []
            for query in (original, restored):
                points = (c.c_float * (4096 * 3))()
                count = c.c_int()
                self.assertEqual(1, lib.sw_recast_copy(query, points, 4096, c.byref(count)))
                paths.append(list(points[:3 * count.value]))
            self.assertEqual(paths[0], paths[1])
            invalid = new_query()
            saved[4] = 0
            self.assertEqual(0, lib.sw_recast_restore_query(invalid, saved, len(saved)))

    def test_sliced_crosses_allowed_semantic_boundary(self):
        self.assertEqual(self.lib.sw_recast_abi(),1)
        self.assertEqual(self.route(self.mesh(),(-5,0,0),(5,0,0),1,2),1)

    def test_directed_connectivity_does_not_assume_reverse(self):
        m=self.mesh(False)
        work=self.lib.sw_recast_connectivity_work(m)
        self.assertGreater(work,0)
        self.assertEqual(self.route(m,(-5,0,0),(5,0,0),1,2),1)
        self.assertEqual(self.route(m,(5,0,0),(-5,0,0),2,1),3)
        self.assertEqual(self.lib.sw_recast_connectivity_work(m),work,"query start must reuse prepared connectivity")

    def test_wrong_layer_is_not_nearest_floor_fallback(self):
        self.assertEqual(self.route(self.mesh(),(-5,0,0),(5,0,0),2,2),5)

    def test_overlapping_floors_preserve_area_and_height(self):
        vertices=(c.c_float*24)(-8,0,-6,8,0,-6,8,0,6,-8,0,6,-8,4,-6,8,4,-6,8,4,6,-8,4,6)
        triangles=(c.c_int*12)(0,2,1,0,3,2,4,6,5,4,7,6)
        areas=(c.c_uint8*4)(1,1,2,2);allowed=(c.c_uint8*4096)();allowed[65]=allowed[130]=1
        m=self.lib.sw_recast_build(vertices,8,triangles,areas,4,allowed,.25,.05,.5,1,.2,45,CANCELLED(lambda: 0))
        self.assertTrue(m);self.addCleanup(self.lib.sw_recast_map_free,m)
        self.assertEqual(self.route(m,(-5,0,0),(5,0,0),1,1),1)
        self.assertEqual(self.route(m,(-5,4,0),(5,4,0),2,2),1)
        self.assertEqual(self.route(m,(-5,0,0),(5,4,0),1,2),3)
        self.assertEqual(self.route(m,(-5,0,0),(5,0,0),2,2),5)

    def test_preparation_cancellation_releases_resources(self):
        before=self.lib.sw_recast_live_maps()
        self.mesh(cancelled=lambda: 1)
        self.assertEqual(self.lib.sw_recast_live_maps(),before)

    def raw_mesh(self, cancel=None):
        v=(c.c_float*12)(-8,0,-6,8,0,-6,8,0,6,-8,0,6)
        t=(c.c_int*6)(0,2,1,0,3,2);a=(c.c_uint8*2)(1,1);allowed=(c.c_uint8*4096)();allowed[65]=1
        return self.lib.sw_recast_build(v,4,t,a,2,allowed,.25,.05,.5,1,.2,45,CANCELLED(cancel or (lambda:0)))

    def test_paid_temporary_bake_denial_and_cancellation_release_original_credits(self):
        l=self.lib;before=l.sw_recast_allocation_count();owner=self.Owner();owner.scope(l)
        m=self.raw_mesh();self.assertTrue(m);total=owner.calls
        self.assertGreater(owner.peak,sum(owner.live.values()),'temporary bake capacity must be admitted')
        self.Owner.clear(l);l.sw_recast_map_free(m);self.assertFalse(owner.live);self.assertFalse(owner.errors)
        for at in (1,2,3,4,total//2,total):
            denied=self.Owner(at);denied.scope(l)
            try:self.assertFalse(self.raw_mesh(),f'denial at allocation {at} was ignored')
            finally:self.Owner.clear(l)
            self.assertFalse(denied.live);self.assertFalse(denied.errors)
        cancelled=self.Owner();cancelled.scope(l)
        try:self.assertFalse(self.raw_mesh(lambda:1))
        finally:self.Owner.clear(l)
        self.assertFalse(cancelled.live);self.assertEqual(l.sw_recast_allocation_count(),before)

    def test_tile_reuse_preserves_routes_and_separates_paid_owners(self):
        lib=self.lib
        baseline=lib.sw_recast_allocation_count()

        def build(previous=None,blocker=False,incremental=True,cancel=None):
            vertices=(-30,0,-12,30,0,-12,30,0,12,-30,0,12)
            triangles=(0,2,1,0,3,2)
            areas=(1,1)
            if blocker:
                vertices+=(-29,0,5,-24,0,5,-24,0,9,-29,0,9)
                triangles+=(4,6,5,4,7,6)
                areas+=(0,0)
            allowed=(c.c_uint8*4096)()
            allowed[65]=1
            args=((c.c_float*len(vertices))(*vertices),len(vertices)//3,
                  (c.c_int*len(triangles))(*triangles),(c.c_uint8*len(areas))(*areas),
                  len(areas),allowed,.25,.05,.5,1,.2,45,CANCELLED(cancel or (lambda:0)))
            if incremental:
                return lib.sw_recast_build_reusing_tiles(*args,previous)
            return lib.sw_recast_build(*args)

        previous_owner=self.Owner()
        next_owner=self.Owner()
        previous=target=cold=None
        try:
            previous_owner.scope(lib)
            previous=build()
            self.Owner.clear(lib)
            self.assertTrue(previous)
            prior=dict(previous_owner.live)
            next_owner.scope(lib)
            target=build(previous,True)
            self.Owner.clear(lib)
            self.assertTrue(target)
            self.assertGreater(lib.sw_recast_reused_tiles(target),0)
            self.assertLess(lib.sw_recast_reused_tiles(target),8)
            self.assertEqual(previous_owner.live,prior)
            cold=build(blocker=True,incremental=False)
            self.assertTrue(cold)
            self.assertEqual(lib.sw_recast_connectivity_work(target),lib.sw_recast_connectivity_work(cold))

            denied=self.Owner(next_owner.calls//2)
            denied.scope(lib)
            self.assertFalse(build(previous,True))
            self.Owner.clear(lib)
            self.assertFalse(denied.live)
            self.assertFalse(denied.errors)
            self.assertEqual(previous_owner.live,prior)

            cancelled=self.Owner()
            checks=iter((0,0,1))
            cancelled.scope(lib)
            self.assertFalse(build(previous,True,cancel=lambda:next(checks,1)))
            self.Owner.clear(lib)
            self.assertFalse(cancelled.live)
            self.assertFalse(cancelled.errors)
            self.assertEqual(previous_owner.live,prior)

            lib.sw_recast_map_free(previous)
            previous=None
            self.assertFalse(previous_owner.live)
            for start,goal in [((-25,0,0),(26,0,0)),((10,0,7),(-20,0,7)),((-20,0,-6),(21,0,7))]:
                actual=self.route(target,start,goal,1,1,details=True)
                self.assertEqual(actual[0],1)
                self.assertEqual(actual,self.route(cold,start,goal,1,1,details=True))
        finally:
            self.Owner.clear(lib)
            for value in (target,cold,previous):
                if value:
                    lib.sw_recast_map_free(value)
        self.assertFalse(next_owner.live)
        self.assertFalse(next_owner.errors)
        self.assertFalse(previous_owner.errors)
        self.assertEqual(lib.sw_recast_allocation_count(),baseline)

    def test_paid_nested_query_denial_and_free_without_tls(self):
        l=self.lib;before=l.sw_recast_allocation_count();outer=self.Owner();outer.scope(l);m=self.raw_mesh();self.assertTrue(m)
        try:
            inner=self.Owner(2);inner.scope(l)
            try:
                q=l.sw_recast_query(m,(c.c_float*3)(-5,0,0),(c.c_float*3)(5,0,0),1,1,1024)
                self.assertFalse(q);self.assertFalse(inner.live)
            finally:outer.scope(l)
            self.assertEqual(self.route(m,(-5,0,0),(5,0,0),1,1),1)
        finally:self.Owner.clear(l);l.sw_recast_map_free(m)
        self.assertFalse(outer.live);self.assertFalse(outer.errors);self.assertFalse(inner.errors)
        self.assertEqual(l.sw_recast_allocation_count(),before)

    def test_paid_step_completion_denial_retains_attached_buffers_until_query_free(self):
        l=self.lib;before=l.sw_recast_allocation_count()
        # Completion grows path, straight, temporary refs and semantic areas.
        # Reject each original allocation, including failures after q owns new buffers.
        for at in range(1,5):
            owner=self.Owner();owner.scope(l);m=self.raw_mesh();self.assertTrue(m)
            q=l.sw_recast_query(m,(c.c_float*3)(-5,0,0),(c.c_float*3)(5,0,0),1,1,1024);self.assertTrue(q)
            step=self.Owner(at);step.scope(l)
            try:
                used=c.c_int();state=l.sw_recast_step(q,4096,c.byref(used))
                self.assertEqual(state,4);self.assertEqual(l.sw_recast_step(q,1,c.byref(used)),4)
            finally:self.Owner.clear(l);l.sw_recast_query_free(q);l.sw_recast_map_free(m)
            self.assertFalse(owner.live);self.assertFalse(step.live)
            self.assertFalse(owner.errors);self.assertFalse(step.errors)
        self.assertEqual(l.sw_recast_allocation_count(),before)

    def test_paid_reentrant_step_survives_failed_outer_build_journal(self):
        l=self.lib;before=l.sw_recast_allocation_count();original=self.Owner();original.scope(l)
        m=self.raw_mesh();self.assertTrue(m)
        q=l.sw_recast_query(m,(c.c_float*3)(-5,0,0),(c.c_float*3)(5,0,0),1,1,1024);self.assertTrue(q)
        outer=self.Owner();outer.scope(l);states=[]
        def cancel():
            used=c.c_int();states.append(l.sw_recast_step(q,4096,c.byref(used)))
            return 1
        try:
            self.assertFalse(self.raw_mesh(cancel));self.assertEqual(states,[1])
            self.assertTrue(outer.live,'preexisting query owns allocations made by nested step')
            used=c.c_int();self.assertEqual(l.sw_recast_step(q,1,c.byref(used)),1)
        finally:self.Owner.clear(l);l.sw_recast_query_free(q);l.sw_recast_map_free(m)
        self.assertFalse(original.live);self.assertFalse(outer.live)
        self.assertFalse(original.errors);self.assertFalse(outer.errors)
        self.assertEqual(l.sw_recast_allocation_count(),before)

    def test_paid_concurrent_owners_and_other_thread_free(self):
        l=self.lib;before=l.sw_recast_allocation_count();rows=[];errors=[]
        def build():
            owner=self.Owner();owner.scope(l)
            try:rows.append((owner,self.raw_mesh()))
            except BaseException as e:errors.append(e)
            finally:self.Owner.clear(l)
        threads=[threading.Thread(target=build) for _ in range(2)]
        for t in threads:t.start()
        for t in threads:t.join()
        self.assertFalse(errors);self.assertEqual(len(rows),2)
        for owner,m in rows:
            self.assertTrue(m);self.assertTrue(owner.live);l.sw_recast_map_free(m)
            self.assertFalse(owner.live);self.assertFalse(owner.errors)
        self.assertEqual(l.sw_recast_allocation_count(),before)
        self.assertEqual(l.sw_recast_allocation_bytes(),0)

    def tearDown(self):
        self.assertEqual(self.lib.sw_recast_live_queries(),0)

if __name__=='__main__': unittest.main()
