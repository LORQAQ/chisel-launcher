import { useEffect } from 'preact/hooks';
import { AccountSheet } from './components/AccountSheet';
import { ConsoleDock } from './components/ConsoleDock';
import { InstallSheet } from './components/InstallSheet';
import { ModpackSheet } from './components/ModpackSheet';
import { InstanceSettingsSheet } from './components/InstanceSettingsSheet';
import { Onboarding } from './components/Onboarding';
import { Sidebar } from './components/Sidebar';
import { TitleBar } from './components/TitleBar';
import { ToastHost } from './components/ToastHost';
import { BrandMark, Icon } from './components/Icon';
import { Button } from './components/ui';
import { ContentPage } from './pages/Content';
import { HomePage } from './pages/Home';
import { SettingsPage } from './pages/Settings';
import { VersionsPage } from './pages/Versions';
import { route } from './lib/router';
import { boot, bootError, consoleOpen, installJob, ready, settings } from './lib/store';

export function App() {
  useEffect(() => {
    void boot();
  }, []);

  const page = route.value;

  useEffect(() => {
    const scroller = document.querySelector('.page-scroll');
    scroller?.scrollTo({ top: 0 });
  }, [page]);

  return (
    <div class="app">
      <TitleBar />
      {!ready.value ? (
        <BootScreen />
      ) : settings.value.onboarded ? (
        <div class="shell">
          <Sidebar />
          <main class="workspace">
            <div class="page-scroll">
              {page === 'home' ? (
                <HomePage />
              ) : page === 'versions' ? (
                <VersionsPage />
              ) : page === 'content' ? (
                <ContentPage />
              ) : (
                <SettingsPage />
              )}
            </div>
            {consoleOpen.value ? <ConsoleDock /> : null}
          </main>
        </div>
      ) : (
        <Onboarding />
      )}

      {ready.value && settings.value.onboarded ? (
        <>
          <AccountSheet />
          <InstallSheet />
          <ModpackSheet />
          <InstanceSettingsSheet />
          <ToastHost />
        </>
      ) : null}
    </div>
  );
}

function BootScreen() {
  const error = bootError.value;
  return (
    <div class="boot">
      <div class="boot__inner">
        <div class="boot__mark">
          <BrandMark size={52} />
          <span class="boot__dot" aria-hidden="true" />
        </div>
        <div class="boot__word serif">凿境</div>
        <div class="boot__latin mono">CHISEL</div>
        {error ? (
          <div class="boot__error">
            <div class="boot__error-title">
              <Icon name="alert" size={15} /> 界面没能连上启动器内核
            </div>
            <div class="boot__error-text">{error}</div>
            <Button variant="ghost" icon="refresh" onClick={() => location.reload()}>
              重新载入
            </Button>
          </div>
        ) : (
          <>
            <div class="boot__line" aria-hidden="true">
              <span />
            </div>
            <div class="boot__hint">正在把纸铺开，把山放好。</div>
          </>
        )}
        {installJob.value?.state === 'running' ? (
          <div class="boot__extra">还有一项准备在后台继续。</div>
        ) : null}
      </div>
    </div>
  );
}
