// IPluginServiceRegistrator was removed in Jellyfin 10.10+.
// Jellyfin auto-discovers IIntroProvider implementations from the plugin assembly via DI.
// The SessionInterceptor is started by PreRollIntroProvider's constructor,
// which is itself auto-discovered and DI-instantiated by Jellyfin at startup.
