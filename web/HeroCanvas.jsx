// HeroCanvas — draggable mini-graph with Run animation.
// Variants: 'graph' (default), 'terminal' (terminal→graph morph), 'banner' (static banner)

const { useState, useEffect, useRef, useCallback } = React;

// Category color map (from GraphTheme)
const CAT = {
  file:    '#2C5A8A',
  process: '#4C9E74',
  out:     '#D4943A',
  flow:    '#7060A8',
  string:  '#708DA3',
};
const PIN = {
  Path: '#76C9A2',         // green
  Collection: '#B8D4EA',   // light blue (wires + outputs)
  Any: '#A8BAC9',          // neutral
  String: '#7FBFEF',       // bright blue
  Bool: '#4E8ECF',         // medium blue
  Num: '#7FBFEF',          // bright blue
  Object: '#B08BD9',       // purple
  Property: '#D4943A',     // amber/orange
  Green: '#76C9A2',
  Blue: '#4E8ECF',
  Purple: '#B08BD9',
  Orange: '#D4943A',
  LBlue: '#B8D4EA',
};

function MiniNode({ node, onDrag, running, runStep, selected, onSelect }) {
  const ref = useRef(null);
  const drag = useRef(null);

  const onPointerDown = (e) => {
    if (e.target.closest('.mini-pin-dot')) return;
    const rect = ref.current.getBoundingClientRect();
    drag.current = {
      offX: e.clientX - rect.left,
      offY: e.clientY - rect.top,
      id: e.pointerId,
    };
    e.currentTarget.setPointerCapture(e.pointerId);
    onSelect?.(node.id);
    e.preventDefault();
  };
  const onPointerMove = (e) => {
    if (!drag.current) return;
    const parent = ref.current.parentElement.getBoundingClientRect();
    const nx = e.clientX - parent.left - drag.current.offX;
    const ny = e.clientY - parent.top - drag.current.offY;
    onDrag(node.id, Math.max(4, nx), Math.max(34, ny));
  };
  const onPointerUp = (e) => {
    drag.current = null;
    try { e.currentTarget.releasePointerCapture(e.pointerId); } catch {}
  };

  // Running pulse: highlight active node
  const isActive = running && runStep === node.runIdx;
  const borderColor = selected ? 'var(--teal-500)'
                    : isActive ? 'var(--amber-400)'
                    : 'var(--border)';
  const outline = selected ? '2px solid var(--teal-500)' : 'none';

  return (
    <div
      ref={ref}
      className="mini-node"
      style={{
        left: node.x, top: node.y, width: node.w,
        borderColor,
        outline,
        outlineOffset: '-2px',
        transition: running ? 'border-color 160ms' : 'none',
        cursor: drag.current ? 'grabbing' : 'grab',
        zIndex: selected ? 5 : 2,
      }}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
    >
      <div className="mini-node-head" style={{ background: node.color }}>
        <span>{node.title}</span>
        <span className="mini-node-chev" aria-hidden="true">▾</span>
      </div>
      <div className="mini-node-flow">
        <span className="mini-flow-tri in" aria-hidden="true" />
        <span className="mini-flow-tri out" aria-hidden="true" />
      </div>
      <div className="mini-node-body">
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {node.ins.map((p, i) => (
            <div key={i} className="mini-pin" style={{ color: PIN[p.t] || PIN.Any }}>
              <span className="mini-pin-dot connected" />
              <span style={{ color: 'var(--fg-tertiary)' }}>{p.name}</span>
            </div>
          ))}
        </div>
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {node.outs.map((p, i) => (
            <div key={i} className="mini-pin out" style={{ color: PIN[p.t] || PIN.Any }}>
              <span style={{ color: 'var(--fg-tertiary)' }}>{p.name}</span>
              <span className="mini-pin-dot connected" />
            </div>
          ))}
        </div>
      </div>
      {node.hidden > 0 && (
        <div className="mini-node-foot">+{node.hidden} hidden</div>
      )}
    </div>
  );
}

// Compute pin absolute position for a wire endpoint
function pinPos(node, side, idx) {
  // Node border 1 + head 28 + flow 22 + body padding 6 + pin row ~24px each; first pin center at 69
  const y = node.y + 71 + idx * 24;
  const x = side === 'out' ? node.x + node.w : node.x;
  return { x, y };
}

function Wire({ from, to, kind, pinColor, running, active, animate }) {
  // Bezier control points — horizontal tangent
  const dx = Math.max(40, Math.abs(to.x - from.x) * 0.5);
  const c1 = { x: from.x + dx, y: from.y };
  const c2 = { x: to.x - dx, y: to.y };
  const d = `M ${from.x} ${from.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${to.x} ${to.y}`;
  const baseColor = pinColor || 'var(--wire)';
  const color = active ? 'var(--amber-400)' : baseColor;
  return (
    <g>
      <path d={d} stroke={color} strokeWidth="2.5" fill="none"
            style={{ transition: 'stroke 200ms' }}
            strokeLinecap="round" />
      {animate && (
        <circle r="3.5" fill={active ? 'var(--amber-300)' : baseColor}>
          <animateMotion dur="2.2s" repeatCount="indefinite" path={d} />
        </circle>
      )}
    </g>
  );
}

function HeroCanvasGraph({ wireFlow, onRunEvent }) {
  const initial = [
    { id:'n1', title:'Get-ChildItem', color: '#26466A',    x: 20,  y: 60,  w: 170,
      ins:[
        {name:'Path',t:'Green'},
        {name:'Filter',t:'Blue'},
        {name:'Include',t:'Green'},
        {name:'Exclude',t:'Green'},
        {name:'Depth',t:'Blue'},
        {name:'Attributes',t:'Purple'},
      ],
      outs:[{name:'FileInfo',t:'LBlue'}],
      hidden: 1, runIdx: 0 },
    { id:'n2', title:'Select-Object', color: '#3E6EA3',   x: 250, y: 190, w: 170,
      ins:[
        {name:'InputObject',t:'Purple'},
        {name:'Property',t:'Orange'},
        {name:'ExcludeProperty',t:'Green'},
        {name:'ExpandProperty',t:'Blue'},
        {name:'Last',t:'Blue'},
        {name:'First',t:'Blue'},
        {name:'Skip',t:'Blue'},
      ],
      outs:[{name:'Out',t:'LBlue'}],
      hidden: 3, runIdx: 1 },
    { id:'n4', title:'Where-Object', color: '#3E6EA3',        x: 480, y: 80, w: 170,
      ins:[
        {name:'InputObject',t:'Purple'},
        {name:'Property',t:'Blue'},
        {name:'Value',t:'Purple'},
      ],
      outs:[{name:'Out',t:'LBlue'}],
      hidden: 1, runIdx: 2 },
  ];
  const [nodes, setNodes] = useState(initial);
  const [selected, setSelected] = useState(null);
  const [running, setRunning] = useState(false);
  const [runStep, setRunStep] = useState(-1);

  const wires = [
    { from:{id:'n1',side:'out',idx:0}, to:{id:'n2',side:'in',idx:0}, t:'LBlue' },
    { from:{id:'n2',side:'out',idx:0}, to:{id:'n4',side:'in',idx:0}, t:'LBlue' },
  ];

  const byId = Object.fromEntries(nodes.map(n => [n.id, n]));
  const getPt = (e) => {
    const n = byId[e.id];
    return pinPos(n, e.side, e.idx);
  };

  // Compute flow-triangle pin position (the ▶ triangles below the header)
  const flowPos = (n, side) => ({
    x: side === 'out' ? n.x + n.w : n.x,
    y: n.y + 1 + 28 + 11, // border + head + mid of 22px flow row
  });
  const flowWires = [
    { from: flowPos(byId['n1'], 'out'), to: flowPos(byId['n2'], 'in') },
    { from: flowPos(byId['n2'], 'out'), to: flowPos(byId['n4'], 'in') },
  ];

  const onDrag = useCallback((id, x, y) => {
    setNodes(ns => ns.map(n => n.id === id ? { ...n, x, y } : n));
  }, []);

  const run = () => {
    if (running) return;
    setRunning(true);
    setRunStep(0);
    onRunEvent?.('start');
    let i = 0;
    const tick = () => {
      i++;
      if (i >= 3) {
        onRunEvent?.('done');
        setTimeout(() => { setRunning(false); setRunStep(-1); }, 800);
        return;
      }
      setRunStep(i);
      setTimeout(tick, 650);
    };
    setTimeout(tick, 650);
  };

  // expose run via window for the chrome Run button
  useEffect(() => {
    window.__pblxHeroRun = run;
    window.__pblxHeroRunning = running;
  }, [running]);

  return (
    <div style={{ position: 'absolute', inset: '30px 0 0 0', overflow: 'hidden', zIndex: 1 }}>
      <svg style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', pointerEvents: 'none' }}>
        {flowWires.map((fw, i) => (
          <Wire key={'f'+i} from={fw.from} to={fw.to} kind="flow"
                pinColor="#B8D4EA"
                running={running} active={running}
                animate={wireFlow || running} />
        ))}
        {wires.map((w, i) => {
          const from = getPt(w.from);
          const to = getPt(w.to);
          const active = running && runStep >= i && runStep <= i + 1;
          return (
            <Wire key={i} from={from} to={to} kind="data"
                  pinColor={PIN[w.t]}
                  running={running} active={active}
                  animate={wireFlow || running} />
          );
        })}
      </svg>
      {nodes.map(n => (
        <MiniNode key={n.id} node={n} onDrag={onDrag}
                  running={running} runStep={runStep}
                  selected={selected === n.id}
                  onSelect={setSelected} />
      ))}
      <div style={{
        position: 'absolute', bottom: 10, left: 12,
        fontSize: 10, color: 'var(--fg-hud)',
        display: 'flex', gap: 10,
        background: 'rgba(11, 25, 41, 0.6)',
        border: '1px solid var(--border)',
        borderRadius: 9999,
        padding: '3px 10px',
      }}>
        <span>drag any node</span>
        <span style={{ color: 'var(--slate-700)' }}>│</span>
        <span>wires retarget live</span>
      </div>
    </div>
  );
}

function HeroCanvasTerminal({ onRunEvent }) {
  // Terminal that morphs into a graph on Run
  const [morphed, setMorphed] = useState(false);
  const lines = [
    { p: '>', t: 'Get-ChildItem C:\\Logs -Filter *.log -Recurse', c: 'var(--teal-300)' },
    { p: '|', t: 'ForEach-Object { ', c: 'var(--purple-300)' },
    { p: '|', t: '  Select-String -Pattern "ERROR"', c: 'var(--slate-400)' },
    { p: '|', t: '}', c: 'var(--purple-300)' },
    { p: '|', t: 'Write-Host -ForegroundColor Red', c: 'var(--amber-300)' },
  ];

  useEffect(() => {
    window.__pblxHeroRun = () => {
      setMorphed(true);
      onRunEvent?.('start');
      setTimeout(() => {
        onRunEvent?.('done');
        setTimeout(() => setMorphed(false), 2000);
      }, 2000);
    };
  }, []);

  return (
    <div style={{ position: 'absolute', inset: '30px 0 0 0', padding: 20, fontSize: 12, lineHeight: 1.8 }}>
      <div style={{
        border: '1px solid var(--border)',
        background: '#060D16',
        borderRadius: 'var(--radius-md)',
        padding: '14px 18px',
        opacity: morphed ? 0.35 : 1,
        transition: 'opacity 400ms',
      }}>
        {lines.map((l, i) => (
          <div key={i} style={{ display: 'flex', gap: 10, color: l.c, fontFamily: 'var(--font-mono)' }}>
            <span style={{ color: 'var(--slate-600)', width: 12 }}>{l.p}</span>
            <span>{l.t}</span>
          </div>
        ))}
        <div style={{ color: 'var(--slate-600)', marginTop: 12, fontSize: 10 }}>
          # ↑ becomes ↓
        </div>
      </div>
      <div style={{
        marginTop: 14,
        border: '1px dashed var(--border-strong)',
        borderRadius: 'var(--radius-md)',
        minHeight: 140,
        padding: 14,
        display: morphed ? 'block' : 'grid',
        placeItems: morphed ? 'initial' : 'center',
        color: 'var(--fg-muted)',
        fontSize: 11,
        position: 'relative',
        opacity: morphed ? 1 : 0.55,
        transition: 'opacity 400ms',
      }}>
        {!morphed && <span>click <b style={{color:'var(--teal-300)'}}>Run</b> to morph into a node graph</span>}
        {morphed && <MiniGraphStatic />}
      </div>
    </div>
  );
}

function MiniGraphStatic() {
  // Compact read-only rendition for terminal-morph variant
  const ns = [
    { id:'a', title:'Get-ChildItem', color: CAT.file,    x: 0,   y: 0,  w: 140,
      ins:[{name:'Path',t:'Path'}], outs:[{name:'Items',t:'Collection'}], badge:'' },
    { id:'b', title:'Where-Object', color: CAT.flow,   x: 170, y: 40, w: 150,
      ins:[{name:'In',t:'Collection'},{name:'Filter',t:'Bool'}], outs:[{name:'Match',t:'Collection'}], badge:'' },
    { id:'c', title:'Select-String', color: CAT.string,  x: 340, y: 0,  w: 140,
      ins:[{name:'In',t:'Any'}], outs:[{name:'Match',t:'Collection'}], badge:'' },
    { id:'d', title:'Write-Host', color: CAT.out,        x: 340, y: 100, w: 140,
      ins:[{name:'Object',t:'Any'}], outs:[], badge:'' },
  ];
  return (
    <div style={{ position: 'relative', height: 180 }}>
      <svg style={{ position: 'absolute', inset: 0, overflow: 'visible' }}>
        <Wire from={pinPos(ns[0],'out',0)} to={pinPos(ns[1],'in',0)} pinColor={PIN.Collection} animate />
        <Wire from={pinPos(ns[1],'out',0)} to={pinPos(ns[2],'in',0)} pinColor={PIN.Any} animate />
        <Wire from={pinPos(ns[2],'out',0)} to={pinPos(ns[3],'in',0)} pinColor={PIN.Collection} animate />
      </svg>
      {ns.map(n => (
        <div key={n.id} className="mini-node" style={{ left: n.x, top: n.y, width: n.w }}>
          <div className="mini-node-head" style={{ background: n.color }}>
            <span>{n.title}</span>
            <span className="mini-node-chev" aria-hidden="true">▾</span>
          </div>
          <div className="mini-node-body">
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              {n.ins.map((p, i) => (
                <div key={i} className="mini-pin" style={{ color: PIN[p.t] || PIN.Any }}>
                  <span className="mini-pin-dot connected" />
                  <span style={{ color: 'var(--fg-tertiary)' }}>{p.name}</span>
                </div>
              ))}
            </div>
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              {n.outs.map((p, i) => (
                <div key={i} className="mini-pin out" style={{ color: PIN[p.t] || PIN.Any }}>
                  <span className="mini-pin-dot connected" />
                  <span style={{ color: 'var(--fg-tertiary)' }}>{p.name}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

function HeroCanvasBanner() {
  // Static banner-style: the brand tile layout, large
  useEffect(() => { window.__pblxHeroRun = null; }, []);
  return (
    <div style={{
      position: 'absolute', inset: '30px 0 0 0',
      display: 'grid', placeItems: 'center',
      padding: 30,
    }}>
      <svg viewBox="0 0 480 320" width="100%" style={{ maxWidth: 480 }}>
        <defs>
          <linearGradient id="hb_bl" x1="0" y1="0" x2="1" y2="0.6">
            <stop offset="0%" stopColor="#3286E8"/><stop offset="100%" stopColor="#1E5BA0"/>
          </linearGradient>
          <linearGradient id="hb_gr" x1="0" y1="0" x2="1" y2="0.6">
            <stop offset="0%" stopColor="#4C9E74"/><stop offset="100%" stopColor="#2E6B4A"/>
          </linearGradient>
          <linearGradient id="hb_or" x1="0" y1="0" x2="1" y2="0.6">
            <stop offset="0%" stopColor="#E8A948"/><stop offset="100%" stopColor="#A87018"/>
          </linearGradient>
        </defs>
        <path d="M 220 100 H 260 V 78 H 308"
              stroke="#4C9E74" strokeWidth="5" fill="none" strokeLinejoin="round" strokeLinecap="round"/>
        <path d="M 220 220 H 280 V 242 H 308"
              stroke="#D4943A" strokeWidth="5" fill="none" strokeLinejoin="round" strokeLinecap="round"/>
        <rect x="70" y="90" width="150" height="140" rx="18" fill="url(#hb_bl)"/>
        <text x="100" y="180" fontFamily="var(--font-mono)" fontSize="58" fontWeight="700" fill="#fff">&gt;_</text>
        <rect x="308" y="46" width="110" height="110" rx="16" fill="url(#hb_gr)"/>
        <text x="333" y="116" fontFamily="var(--font-mono)" fontSize="44" fontWeight="700" fill="#fff" opacity="0.9">⚙</text>
        <rect x="308" y="210" width="110" height="110" rx="16" fill="url(#hb_or)"/>
        <text x="340" y="278" fontFamily="var(--font-mono)" fontSize="42" fontWeight="700" fill="#fff" opacity="0.9">»</text>
      </svg>
    </div>
  );
}

function HeroCanvas({ variant = 'graph', wireFlow, onRunEvent }) {
  if (variant === 'terminal') return <HeroCanvasTerminal onRunEvent={onRunEvent} />;
  if (variant === 'banner')   return <HeroCanvasBanner />;
  return <HeroCanvasGraph wireFlow={wireFlow} onRunEvent={onRunEvent} />;
}

Object.assign(window, { HeroCanvas, MiniNode, Wire, pinPos, CAT, PIN });
