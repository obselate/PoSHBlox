// Main App — assembles nav, hero, sections, footer, tweaks panel.

const { useState: useS, useEffect: useE, useRef: useR } = React;

const GITHUB_URL = 'https://github.com/obselate/PoSHBlox';
const VERSION = 'v0.6.0';

// Tiny inline icons (Lucide-style stroke-1.5)
const Icon = ({ name, size = 14, color = 'currentColor' }) => {
  const P = {
    download: <><path d="M12 3v12" /><path d="m7 10 5 5 5-5" /><path d="M5 21h14" /></>,
    github: <><path d="M9 19c-4.3 1.4-4.3-2.5-6-3m12 5v-3.5a3.4 3.4 0 0 0-1-2.6c3.4-.4 6.9-1.7 6.9-7.5A5.9 5.9 0 0 0 19.4 3a5.5 5.5 0 0 0-.1-4s-1.3-.4-4.2 1.6a14.7 14.7 0 0 0-7.4 0C4.8-1.4 3.4-1 3.4-1a5.5 5.5 0 0 0-.1 4A5.9 5.9 0 0 0 2 7.4c0 5.7 3.4 7.1 6.9 7.5a3.4 3.4 0 0 0-1 2.6V21" /></>,
    play: <><polygon points="6 4 20 12 6 20 6 4" fill="currentColor" stroke="none" /></>,
    arrow: <><path d="M5 12h14" /><path d="m12 5 7 7-7 7" /></>,
    code: <><path d="m16 18 6-6-6-6" /><path d="m8 6-6 6 6 6" /></>,
    spark: <><path d="M12 3v5M12 16v5M3 12h5M16 12h5M5.6 5.6l3.5 3.5M14.9 14.9l3.5 3.5M5.6 18.4l3.5-3.5M14.9 9.1l3.5-3.5" /></>,
    cmd: <><path d="M15 6h3a3 3 0 1 1 0 6h-3v-6zM6 6h3v6H6a3 3 0 1 1 0-6zM15 12h3a3 3 0 1 1 0 6h-3v-6zM6 12h3v6H6a3 3 0 1 1 0-6z" /></>,
    box: <><path d="M21 16V8a2 2 0 0 0-1-1.7l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.7l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" /><path d="m3.3 7 8.7 5 8.7-5" /><path d="M12 22V12" /></>,
    export: <><path d="M14 3h7v7" /><path d="M21 3 10 14" /><path d="M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5" /></>,
    keyboard: <><rect x="2" y="4" width="20" height="16" rx="2" /><path d="M6 8h.01M10 8h.01M14 8h.01M18 8h.01M6 12h.01M10 12h.01M14 12h.01M18 12h.01M7 16h10" /></>
  };
  return (
    <svg width={size} height={size} viewBox="0 0 24 24"
    fill="none" stroke={color} strokeWidth="1.5"
    strokeLinecap="round" strokeLinejoin="round">
      {P[name]}
    </svg>);

};

function Nav() {
  return (
    <nav className="nav">
      <div className="nav-inner">
        <div className="nav-mark">
          <div className="nav-mark-tile" aria-label="PoSHBlox" />
          <span className="nav-name">PoSH<span className="blox">Blox</span></span>
          <span className="nav-version">{VERSION}</span>
        </div>
        <div className="nav-links">
          <a href="#what" style={{ fontSize: "14px", fontWeight: "600" }}>What</a>
          <a href="#how" style={{ fontWeight: "600", fontSize: "14px" }}>How</a>
          <a href="#features" style={{ fontSize: "14px", fontWeight: "600" }}>Features</a>
          <a href={GITHUB_URL} style={{ fontSize: "14px", fontWeight: "600" }}>GitHub</a>
        </div>
        <a href={GITHUB_URL + '/releases'} className="nav-cta">
          <Icon name="download" size={12} /> Download
        </a>
      </div>
    </nav>);

}

function Hero({ variant, wireFlow }) {
  const [running, setRunning] = useS(false);
  const [lastEvt, setLastEvt] = useS('');
  const [scriptOpen, setScriptOpen] = useS(false);
  const onEvt = (e) => {
    if (e === 'start') {setRunning(true);setLastEvt('running...');}
    if (e === 'done') {setRunning(false);setLastEvt('exit 0 · 3 matches');}
  };
  const runClick = () => {window.__pblxHeroRun && window.__pblxHeroRun();};

  return (
    <section className="hero" id="hero">
      <div>
        <div className="hero-eyebrow">
          PowerShell · Visual Scripting · Windows
        </div>
        <h1>
          Drag.<br />
          Connect.<br />
          <span className="tl">Run.</span>
        </h1>
        <p className="hero-sub">
          PoSHBlox is a node-graph editor for PowerShell. Wire cmdlets like
          <code> Get-Process</code> and <code>Invoke-WebRequest</code> on a canvas, hit
          <code> F5</code>, and it generates real PowerShell.
        </p>
        <div className="hero-ctas">
          <a href={GITHUB_URL + '/releases'} className="btn btn-run">
            <Icon name="download" size={14} /> Download {VERSION}
          </a>
          <a href={GITHUB_URL} className="btn btn-ghost">
            <Icon name="github" size={14} /> View source
          </a>
        </div>
        <div className="hero-stats">
          <div><b>85+</b> built-in cmdlets</div>
          <div><b>12</b> categories</div>
          <div><b>0</b> runtime dependencies</div>
        </div>
      </div>
      <div className="hero-visual">
        <div className="hero-visual-chrome">
          <div className="hc-tile" aria-label="PoSHBlox" />
          <span className="hc-title">PoSHBlox</span>
          <span style={{ color: 'var(--slate-700)' }}>—</span>
          <span>demo.pblx</span>
          <button
            className={'hc-script' + (scriptOpen ? ' on' : '')}
            onClick={() => setScriptOpen((v) => !v)}
            title="Toggle script panel"
            aria-pressed={scriptOpen}>
            
            <span>Script Panel</span>
            <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700 }}>{'{ }'}</span>
          </button>
          <button className={'hc-run' + (running ? ' running' : '')} onClick={runClick} style={{ fontFamily: "\"JetBrains Mono\"" }}>
            <Icon name="play" size={9} /> {running ? 'running' : 'Run (F5)'}
          </button>
          <div className="win-ctrls">
            <button className="win-btn" aria-label="Minimize">
              <svg viewBox="0 0 10 10"><path d="M0 5h10" stroke="currentColor" strokeWidth="1" /></svg>
            </button>
            <button className="win-btn" aria-label="Maximize">
              <svg viewBox="0 0 10 10"><rect x="0.5" y="0.5" width="9" height="9" fill="none" stroke="currentColor" strokeWidth="1" /></svg>
            </button>
            <button className="win-btn win-close" aria-label="Close">
              <svg viewBox="0 0 10 10"><path d="M0 0l10 10M10 0L0 10" stroke="currentColor" strokeWidth="1" /></svg>
            </button>
          </div>
        </div>
        <HeroCanvas variant={variant} wireFlow={wireFlow} onRunEvent={onEvt} />
        <div className={'hero-script-panel' + (scriptOpen ? ' open' : '')}>
          <div className="hsp-head">
            <span className="hsp-title">Script Preview</span>
            <button className="hsp-close" onClick={() => setScriptOpen(false)} aria-label="Collapse">▾</button>
          </div>
          <pre className="hsp-code"><code>
<span className="ps-cm">Get-ChildItem</span> <span className="ps-flag">-Path</span> @(<span className="ps-str">"C:\Logs"</span>) <span className="ps-flag">-Filter</span> <span className="ps-str">"*.log"</span> <span className="ps-flag">-Recurse</span> <span className="ps-op">|</span>{'\n'}
{'  '}<span className="ps-cm">Where-Object</span> {'{'} <span className="ps-var">$_</span>.<span className="ps-prop">Length</span> <span className="ps-op">-gt</span> <span className="ps-num">0</span> {'}'} <span className="ps-op">|</span>{'\n'}
{'  '}<span className="ps-cm">Write-Host</span> <span className="ps-flag">-ForegroundColor</span> <span className="ps-str">"Cyan"</span>{'\n'}
          </code></pre>
        </div>
        {lastEvt && !running &&
        <div style={{
          position: 'absolute', bottom: 10, right: 12,
          fontSize: 10, color: 'var(--teal-300)',
          background: 'rgba(11, 25, 41, 0.8)',
          border: '1px solid var(--teal-700)',
          padding: '3px 10px', borderRadius: 9999, zIndex: 4
        }}>✓ {lastEvt}</div>
        }
      </div>
    </section>);

}

function WhatItIs() {
  const cells = [
  { tag: 'FILE / FOLDER', color: '#2C5A8A', h: 'It is a node editor.',
    p: 'A canvas, a palette of cmdlets, and Bezier wires between them. Built on Avalonia + .NET 10, looks like the IDE you wish shipped with Windows.' },
  { tag: 'PROCESS / SERVICE', color: '#4C9E74', h: 'It is a real PowerShell tool.',
    p: 'Every node maps to an actual cmdlet. Parameters bind to real types. Keybinds to speed up your workflow, Undo/Redo for all actions, and bulk selections for re-arranging large workflows.' },
  { tag: 'OUTPUT', color: '#D4943A', h: 'You get a plain .ps1 file.',
    p: 'Hit export and PoSHBlox writes a regular PowerShell script. Commit it, schedule it, share it. Nothing in the file depends on PoSHBlox.' }];

  return (
    <section className="panel page" id="what">
      <div className="section-label"><span className="tag-num">01</span> WHAT IS POSHBLOX</div>
      <h2 className="section-title">Building blocks for PowerShell.</h2>
      <p className="section-lede">
        PoSHBlox is a visual way to learn PowerShell and a modern replacement for the
        retired <code>PowerShell ISE</code>. See the pipeline before you write it — every node
        is a real cmdlet, every wire is a real parameter bind.
      </p>
      <div className="pitch-grid">
        {cells.map((c, i) =>
        <div className="pitch-cell" key={i} style={{ textAlign: "left" }}>
            <div className="cell-tag">
              <span className="dot" style={{ background: c.color }} /> {c.tag}
            </div>
            <h3>{c.h}</h3>
            <p>{c.p}</p>
          </div>
        )}
      </div>
    </section>);

}

function HowItWorks() {
  const steps = [
  { n: '01', h: 'Pick a cmdlet.',
    p: 'Press P for the palette. Search by name or category — File, Process, Network, Control Flow, Output. Drag the node onto the canvas.',
    V: StepVisualPalette },
  { n: '02', h: 'Wire the pins.',
    p: 'Triangular exec pins order execution; colored data pins carry typed values. Pin colors encode type — amber is Bool, sand is Collection, cyan is Int.',
    V: StepVisualWire },
  { n: '03', h: 'Hit Run.',
    p: 'F5 spawns pwsh and streams the output. Ctrl+E dumps the generated script to a .ps1. The graph stays the source of truth; the file is the receipt.',
    V: StepVisualRun }];

  return (
    <section className="panel page" id="how">
      <div className="section-label"><span className="tag-num">02</span> HOW IT WORKS</div>
      <h2 className="section-title">Three verbs. In order.</h2>
      <p className="section-lede">
        Build the graph, run it to see the result, then export to a real
        <code> .ps1</code> file you can save, share, or run from the terminal.
      </p>
      <div className="steps">
        {steps.map((s, i) =>
        <div className="step" key={i}>
            <div className="step-head">
              <span className="step-num">{s.n}</span>
            </div>
            <h3>{s.h}</h3>
            <p>{s.p}</p>
            <div className="step-vis"><s.V /></div>
          </div>
        )}
      </div>
    </section>);

}

function Features() {
  const f = [
  { icon: 'cmd', tag: 'Cmdlets', c: '#4C9E74', h: '85+ built-in, typed.',
    p: 'Every node is a real cmdlet. Parameters are typed; pins won\'t connect if the types don\'t match. No silent coercion.',
    foot: 'Import-Module to add your own.' },
  { icon: 'box', tag: 'Containers', c: '#7060A8', h: 'If/Else · ForEach · Try/Catch.',
    p: 'Flow-control containers hold child nodes inside a dashed zone. The generated script respects your indentation, not just your intent.',
    foot: 'Nest as deep as the graph is readable.' },
  { icon: 'export', tag: 'Export', c: '#2C5A8A', h: 'Ctrl+E → real .ps1.',
    p: 'The output is straight PowerShell — no wrapper, no import, no runtime. Commit it. Diff it. Review it in the tool your team already uses.',
    foot: 'Round-trip back into PoSHBlox on roadmap.' },
  { icon: 'keyboard', tag: 'Keyboard', c: '#5BA89A', h: 'Every action has a key.',
    p: 'P for palette, / for search, F5 to run, Ctrl+E to export, ? for the sheet. The mouse is for dragging nodes, not hunting menus.',
    foot: 'Never breaks muscle memory.' },
  { icon: 'spark', tag: 'Theme', c: '#D4943A', h: 'Dark. One ramp. No toggle.',
    p: 'A deep-steel canvas with teal accents and category-coded nodes. Tuned for long sessions in front of a Windows terminal.',
    foot: 'Built for Windows.' },
  { icon: 'code', tag: 'Open source', c: '#708DA3', h: 'AGPL-3.0 · .NET 10 · Avalonia.',
    p: 'Built in C# on FluentAvalonia. The codebase is readable, the theme tokens live in one AXAML, and the renderer is two files. Fork away.',
    foot: 'PRs welcome.' }];

  return (
    <section className="panel page" id="features">
      <div className="section-label"><span className="tag-num">03</span> FEATURES</div>
      <h2 className="section-title">Six things it does well.<br /><span style={{ color: 'var(--fg-muted)' }}>And nothing it doesn't.</span></h2>
      <div className="features">
        {f.map((x, i) =>
        <div className="feature" key={i}>
            <div className="feature-tag">
              <span className="sq" style={{ background: x.c }} /> {x.tag}
              <span style={{ marginLeft: 'auto', color: 'var(--slate-700)' }}>
                <Icon name={x.icon} size={14} />
              </span>
            </div>
            <h4>{x.h}</h4>
            <p>{x.p}</p>
            <div className="feature-foot">
              <span style={{ width: 4, height: 4, borderRadius: '50%', background: x.c }} />
              {x.foot}
            </div>
          </div>
        )}
      </div>
    </section>);

}

function DownloadCTA() {
  return (
    <section className="panel page" style={{ marginTop: 40 }}>
      <div className="download-cta">
        <div>
          <div className="section-label" style={{ color: 'var(--teal-400)' }}>
            <span style={{ color: 'var(--teal-400)' }}>→</span> GET IT
          </div>
          <h2 className="section-title" style={{ marginBottom: 12 }}>
            Latest release: <span style={{ color: 'var(--teal-400)' }}>{VERSION}</span>
          </h2>
          <p style={{ color: 'var(--fg-muted)', fontSize: 13, margin: 0, maxWidth: '72ch', lineHeight: 1.55 }}>
            Windows x64 release zip
          </p>
        </div>
        <div className="download-cta-buttons">
          <a href={GITHUB_URL + '/releases/latest'} className="btn btn-run" style={{ justifyContent: 'center' }}>
            <Icon name="download" size={14} /> PoSHBlox-{VERSION}-win-x64.exe
          </a>
          <a href={GITHUB_URL + '/releases'} className="btn btn-ghost" style={{ justifyContent: 'center' }}>
            All releases <Icon name="arrow" size={12} />
          </a>
        </div>
      </div>
    </section>);

}

function Footer() {
  return (
    <footer className="foot">
      <div className="foot-inner">
        <div>
          <div className="foot-mark" style={{ gap: "0px" }}>
            <div className="nav-mark-tile" aria-label="PoSHBlox" />
            &nbsp;PoSH<span className="blox">Blox</span>
          </div>
          <p className="foot-desc">A visual node-graph editor for PowerShell


          </p>
        </div>
        <div className="foot-col">
          <h5>Project</h5>
          <a href={GITHUB_URL}>Source</a>
          <a href={GITHUB_URL + '/releases'}>Releases</a>
          <a href={GITHUB_URL + '/issues'}>Issues</a>
          <a href={GITHUB_URL + '/blob/main/CONTRIBUTING.md'}>Contributing</a>
        </div>
        <div className="foot-col">
          <h5>Credits</h5>
          <a href="https://github.com/obselate">@obselate — author</a>
          <a href="https://www.jetbrains.com/lp/mono/">JetBrains Mono</a>
          <a href="https://avaloniaui.net/">Avalonia UI</a>
          <a href="https://lucide.dev/">Lucide (web icons)</a>
        </div>
        <div className="foot-col">
          <h5>Contact</h5>
          <a href={GITHUB_URL + '/discussions'}>GitHub Discussions</a>
          <a href={GITHUB_URL + '/issues/new'}>File a bug</a>
          <a href="mailto:hi@poshblox.dev">hi@poshblox.dev</a>
        </div>
      </div>
      <div className="foot-bar">
        <span>AGPL-3.0 License · © 2026 <a href="https://github.com/obselate" style={{ color: 'var(--teal-300)' }}>obselate</a></span>
      </div>
    </footer>);

}

function App() {
  return (
    <>
      <Nav />
      <Hero variant="graph" wireFlow={true} />
      <WhatItIs />
      <HowItWorks />
      <Features />
      <DownloadCTA />
      <Footer />
    </>);

}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);