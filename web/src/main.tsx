import { render } from 'preact';
import { App } from './app';
import { initBridge } from './lib/bridge';
import './styles/global.css';
import './styles/shell.css';
import './styles/components.css';
import './styles/pages.css';
import './styles/redesign.css';

async function start() {
  await initBridge();
  const host = document.getElementById('app');
  if (host) render(<App />, host);
}

void start();
