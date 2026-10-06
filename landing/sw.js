// Retire the previous browser game's worker without deleting caches or reloading matches.
// Once activated, future requests use the network because this worker has no fetch handler.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  await self.clients.claim();
  await self.registration.unregister();
})()));
