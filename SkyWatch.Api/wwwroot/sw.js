// Observable Smarts Service Worker stub — PWA installability
const CACHE_NAME = 'observable-smarts-v1';

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(clients.claim());
});

self.addEventListener('fetch', (event) => {
    // Pass through all requests (no offline caching for v1)
    event.respondWith(fetch(event.request));
});
