// Small illustrative visuals for each "How it works" step.

function StepVisualPalette() {
  const cats = [
    { name: 'Recent',       c: '#D17C8A', open: false },
    { name: 'Control Flow', c: '#7060A8', open: false },
    { name: 'File / Folder', c: '#2C5A8A', open: true },
  ];
  const entries = [
    { n: 'Add-Content',   d: 'Adds content to the specified items.' },
    { n: 'Copy-Item',     d: 'Copies an item from one location to another.' },
  ];
  return (
    <div style={{
      padding: 6,
      fontFamily: 'var(--font-sans)',
      color: 'var(--fg)',
      display: 'flex', flexDirection: 'column', gap: 5,
    }}>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div style={{ fontSize: 10, fontWeight: 700 }}>
          Node Palette <span style={{ color: 'var(--fg-muted)', fontWeight: 400, fontSize: 8 }}>(P)</span>
        </div>
        <div style={{
          fontSize: 7.5, padding: '1.5px 5px',
          border: '1px solid var(--border)',
          borderRadius: 3, color: 'var(--fg-tertiary)',
          background: 'var(--bg-secondary)',
        }}>+ Import</div>
      </div>

      {/* Search box */}
      <div style={{
        fontSize: 8.5, color: 'var(--fg-muted)',
        border: '1px solid var(--border)',
        background: 'var(--bg-secondary)',
        borderRadius: 3, padding: '3px 6px',
      }}>
        Search commands... <span style={{ opacity: 0.7 }}>(/)</span>
      </div>

      {/* Category rows */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {cats.map(c => (
          <React.Fragment key={c.name}>
            <div style={{
              display: 'flex', alignItems: 'center', gap: 5,
              padding: '2px 4px', fontSize: 8.5,
              color: c.open ? 'var(--fg)' : 'var(--fg-tertiary)',
              borderLeft: `2px solid ${c.c}`,
            }}>
              <span style={{ fontSize: 6.5, color: 'var(--fg-muted)' }}>
                {c.open ? '▼' : '▶'}
              </span>
              <span style={{ fontWeight: c.open ? 600 : 400 }}>{c.name}</span>
            </div>
            {c.open && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 2, paddingLeft: 4, marginTop: 2 }}>
                {entries.map((e, j) => (
                  <div key={j} style={{
                    border: '1px solid var(--border)',
                    borderRadius: 3,
                    padding: '3px 6px',
                    background: j === 0 ? '#11263B' : 'transparent',
                  }}>
                    <div style={{ fontSize: 8.5, fontWeight: 600, color: 'var(--fg)', fontFamily: 'var(--font-mono)' }}>
                      {e.n}
                    </div>
                    <div style={{ fontSize: 7.5, color: 'var(--fg-muted)', lineHeight: 1.35, marginTop: 1 }}>
                      {e.d}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </React.Fragment>
        ))}
      </div>
    </div>
  );
}

function StepVisualWire() {
  // Mini versions of the hero nodes: head + flow-triangle row + pin rows.
  const LB = '#B8D4EA';
  const PURPLE = '#B08BD9';
  const GREEN = '#76C9A2';
  const BLUE = '#4E8ECF';
  const ORANGE = '#D4943A';
  const nodes = [
    {
      x: 18, y: 18, w: 125, color: '#26466A', title: 'Get-ChildItem',
      ins: [
        { name: 'Path',   c: GREEN },
        { name: 'Filter', c: BLUE },
        { name: 'Depth',  c: BLUE },
      ],
      outs: [{ name: 'FileInfo', c: LB }],
    },
    {
      x: 248, y: 72, w: 125, color: '#3E6EA3', title: 'Select-Object',
      ins: [
        { name: 'Input',    c: PURPLE },
        { name: 'Property', c: ORANGE },
        { name: 'First',    c: BLUE },
      ],
      outs: [{ name: 'Out', c: LB }],
    },
  ];
  // Head 18, flow 12, body pad-top 3, pin row 14. First pin center = top + 18 + 12 + 3 + 7.
  const HEAD = 18, FLOW = 12, PIN = 14, PAD = 3;
  const pinY = (n, idx) => n.y + HEAD + FLOW + PAD + 7 + idx * PIN;
  const pinX = (n, side) => side === 'out' ? n.x + n.w : n.x;
  const flowY = (n) => n.y + HEAD + FLOW / 2;
  const from = { x: pinX(nodes[0], 'out'), y: pinY(nodes[0], 0) };
  const to   = { x: pinX(nodes[1], 'in'),  y: pinY(nodes[1], 0) };
  const fFrom = { x: pinX(nodes[0], 'out'), y: flowY(nodes[0]) };
  const fTo   = { x: pinX(nodes[1], 'in'),  y: flowY(nodes[1]) };
  const pathFor = (a, b) => {
    const dx = Math.max(22, Math.abs(b.x - a.x) * 0.5);
    return `M ${a.x} ${a.y} C ${a.x + dx} ${a.y}, ${b.x - dx} ${b.y}, ${b.x} ${b.y}`;
  };
  const dData = pathFor(from, to);
  const dFlow = pathFor(fFrom, fTo);
  return (
    <div style={{ position: 'relative', width: '100%', height: '100%' }}>
      <svg style={{ position: 'absolute', inset: 0, overflow: 'visible' }}>
        <path d={dFlow} stroke={LB} strokeWidth="1.8" fill="none"/>
        <path d={dData} stroke={LB} strokeWidth="2" fill="none"/>
        <circle r="2.4" fill={LB}>
          <animateMotion dur="1.8s" repeatCount="indefinite" path={dData} />
        </circle>
      </svg>
      {nodes.map((n, i) => (
        <div key={i} style={{
          position: 'absolute', left: n.x, top: n.y, width: n.w,
          borderRadius: 4,
          background: 'var(--bg-secondary)',
          border: '1px solid var(--border)',
          boxShadow: 'var(--shadow-node)',
          fontFamily: 'var(--font-mono)',
        }}>
          {/* Head */}
          <div style={{
            height: HEAD, background: n.color,
            color: '#fff', fontSize: 9, fontWeight: 700,
            padding: '0 6px', display: 'flex', alignItems: 'center',
            justifyContent: 'space-between',
            borderBottom: '1px solid rgba(0,0,0,0.35)',
          }}>
            <span>{n.title}</span>
            <span style={{ opacity: 0.7, fontSize: 7 }}>▾</span>
          </div>
          {/* Flow row */}
          <div style={{
            height: FLOW, display: 'flex', justifyContent: 'space-between',
            alignItems: 'center', padding: '0 4px',
            background: 'rgba(0,0,0,0.15)',
            borderBottom: '1px solid rgba(255,255,255,0.04)',
          }}>
            <span style={{
              width: 0, height: 0,
              borderTop: '3.5px solid transparent',
              borderBottom: '3.5px solid transparent',
              borderLeft: `5px solid ${LB}`,
            }}/>
            <span style={{
              width: 0, height: 0,
              borderTop: '3.5px solid transparent',
              borderBottom: '3.5px solid transparent',
              borderLeft: `5px solid ${LB}`,
            }}/>
          </div>
          {/* Pin rows */}
          <div style={{ padding: '3px 8px 5px', position: 'relative' }}>
            {Array.from({ length: Math.max(n.ins.length, n.outs.length) }).map((_, r) => {
              const inP = n.ins[r]; const outP = n.outs[r];
              const DOT = 10;
              const EDGE = -DOT/2 - 8 - 1; /* dot half + parent padding + node border */
              return (
                <div key={r} style={{
                  height: PIN, display: 'flex', alignItems: 'center',
                  justifyContent: 'space-between',
                  color: 'var(--fg-tertiary)', fontSize: 8.5,
                  position: 'relative',
                }}>
                  {inP && (
                    <span style={{
                      position: 'absolute', left: EDGE, top: '50%',
                      transform: 'translateY(-50%)',
                      width: DOT, height: DOT, borderRadius: '50%',
                      border: `2px solid ${inP.c}`,
                      background: inP.c,
                      boxSizing: 'border-box',
                    }}/>
                  )}
                  <span>{inP ? inP.name : ''}</span>
                  <span>{outP ? outP.name : ''}</span>
                  {outP && (
                    <span style={{
                      position: 'absolute', right: EDGE, top: '50%',
                      transform: 'translateY(-50%)',
                      width: DOT, height: DOT, borderRadius: '50%',
                      border: `2px solid ${outP.c}`,
                      background: outP.c,
                      boxSizing: 'border-box',
                    }}/>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

function StepVisualRun() {
  const teal = 'var(--teal-300)';
  return (
    <div style={{
      padding: '14px 14px',
      fontSize: 11, color: 'var(--fg-tertiary)',
      fontFamily: 'var(--font-mono)',
      background: '#060D16',
      height: '100%',
      overflow: 'hidden',
      lineHeight: 1.6,
      display: 'flex', flexDirection: 'column', justifyContent: 'space-between',
    }}>
      <div>
        <div style={{ color: 'var(--slate-600)', fontSize: 10, marginBottom: 2 }}># generated.ps1</div>
        <div><span style={{ color: teal, fontWeight: 600 }}>Get-ChildItem</span>
             <span style={{ color: teal }}> -Path</span>
             <span style={{ color: teal }}> "C:\Logs"</span>
             <span style={{ color: teal }}> -Recurse</span>
        </div>
        <div style={{ color: teal }}>{' |'} <span style={{ fontWeight: 600 }}>ForEach-Object</span> {`{`}</div>
        <div>&nbsp;&nbsp;<span style={{ color: teal, fontWeight: 600 }}>Select-String</span>
             <span style={{ color: teal }}> -Pattern</span>
             <span style={{ color: teal }}> "ERROR"</span>
        </div>
        <div style={{ color: teal }}>{`}`}</div>
      </div>
      <div>
        <div style={{
          borderTop: '1px dashed var(--slate-700)',
          paddingTop: 6, marginTop: 4,
          color: 'var(--slate-500)', fontSize: 9.5,
        }}>
          <div>→ app.log:42 ERROR connection timeout</div>
          <div>→ sys.log:118 ERROR drive unmounted</div>
        </div>
        <div style={{ color: 'var(--fg-muted)', marginTop: 6, fontSize: 9 }}>↳ F5 runs · Ctrl+E exports</div>
      </div>
    </div>
  );
}

Object.assign(window, { StepVisualPalette, StepVisualWire, StepVisualRun });
