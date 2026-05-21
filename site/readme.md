---
title: Home
layout: simple
og_type: website
---

<section class="text-center py-5 codealta-hero">
  <div class="container">
    <pre class="codealta-ascii-logo mx-auto" aria-label="CodeAlta"><span class="logo-code">   ██████                  ██           </span><span class="logo-alta">     ██       ██    ██</span>
<span class="logo-code">  ██░░░░██                ░██           </span><span class="logo-alta">    ████     ░██   ░██</span>
<span class="logo-code"> ██    ░░    ██████       ░██   █████   </span><span class="logo-alta">   ██░░██    ░██  ██████   ██████</span>
<span class="logo-code">░██         ██░░░░██   ██████  ██░░░██  </span><span class="logo-alta">  ██  ░░██   ░██ ░░░██░   ░░░░░░██</span>
<span class="logo-code">░██        ░██   ░██  ██░░░██ ░███████  </span><span class="logo-alta"> ██████████  ░██   ░██     ███████</span>
<span class="logo-code">░░██    ██ ░██   ░██ ░██  ░██ ░██░░░░   </span><span class="logo-alta">░██░░░░░░██  ░██   ░██    ██░░░░██</span>
<span class="logo-code"> ░░██████  ░░██████  ░░██████ ░░██████  </span><span class="logo-alta">░██     ░██  ███   ░░██  ░░████████</span>
<span class="logo-code">  ░░░░░░    ░░░░░░    ░░░░░░   ░░░░░░   </span><span class="logo-alta">░░      ░░  ░░░     ░░    ░░░░░░░░</span></pre>
    <p class="lead mt-4 mb-4">
      A keyboard-first, terminal AI coding workspace for managing projects, model providers, threads, plugins, and delegated agents.
    </p>
    <div class="d-flex justify-content-center gap-3 mt-4 flex-wrap">
      <a href="{{site.basepath}}/docs/getting-started/" class="btn btn-primary btn-lg"><i class="bi bi-rocket-takeoff"></i> Get started</a>
      <a href="{{site.basepath}}/docs/model-providers/" class="btn btn-outline-secondary btn-lg"><i class="bi bi-cpu"></i> Configure providers</a>
      <a href="https://github.com/CodeAlta/CodeAlta" class="btn btn-info btn-lg"><i class="bi bi-github"></i> GitHub</a>
    </div>
    <div class="mt-4 text-start mx-auto" style="max-width: 48rem;">
      <pre class="language-shell-session"><code>dotnet tool install -g CodeAlta
alta</code></pre>
      <p class="text-center text-secondary mt-2" style="font-size: 0.85rem;">The NuGet package is <a href="https://www.nuget.org/packages/CodeAlta/" class="text-secondary">CodeAlta</a>; the installed command is <code>alta</code>. Requires <a href="https://dotnet.microsoft.com/en-us/download/dotnet/10.0" class="text-secondary">.NET 10</a>.</p>
    </div>
  </div>
</section>

<section class="container my-5">
  <div class="card overflow-hidden">
    <div class="card-header display-6"><i class="bi bi-play-circle lunet-feature-icon lunet-icon--controls"></i> Terminal workflow preview</div>
    <div class="card-body">
      <p class="card-text">A short usage video will be embedded here to show first launch, provider setup, prompt sending, tool calls, and timeline cards.</p>
      <div class="terminal-demo-placeholder" role="img" aria-label="CodeAlta terminal demo placeholder">
        <div class="demo-titlebar"><span></span><span></span><span></span><strong>alta</strong></div>
        <pre><code>Projects  ▸ CodeAlta
Threads   ▸ Fix parser test

provider: Codex · gpt-5.5 · reasoning high · ctx 18%

&gt; Use the smallest safe change and run the focused test.

assistant  Planning the change…
tool       read_file src/CodeAlta/...
result     1 file modified · +12 -3</code></pre>
      </div>
      <!-- Replace the placeholder above with a video element when the demo capture is available. -->
    </div>
  </div>
</section>

<section class="container my-5">
  <div class="text-center mb-4">
    <p class="text-uppercase text-secondary fw-semibold mb-2">CodeAlta principles</p>
    <h2 class="display-6 mb-3">Efficient. Transparent. Keyboard-first. Thread-oriented. Provider-agnostic. Native .NET. Error-aware. Pluggable.</h2>
    <p class="lead mb-0">These principles guide the product design without turning the workspace into a marketing surface.</p>
  </div>
  <div class="row row-cols-1 row-cols-lg-2 gx-4 gy-4">
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-arrows-collapse lunet-feature-icon lunet-icon--layout"></i> Efficient interface</div>
        <div class="card-body">
          CodeAlta uses terminal space efficiently.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-eye lunet-feature-icon lunet-icon--data"></i> Transparent execution</div>
        <div class="card-body">
          CodeAlta keeps agent execution inspectable.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-keyboard lunet-feature-icon lunet-icon--controls"></i> Keyboard-first workflow</div>
        <div class="card-body">
          CodeAlta supports normal work from the keyboard.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-diagram-3 lunet-feature-icon lunet-icon--binding"></i> Thread-oriented workspace</div>
        <div class="card-body">
          CodeAlta models agent work as durable threads rather than disposable chat scrollback.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-cpu lunet-feature-icon lunet-icon--controls"></i> Provider-agnostic runtime</div>
        <div class="card-body">
          CodeAlta models LLM execution as providers, not as a single-vendor integration.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-braces-asterisk lunet-feature-icon lunet-icon--data"></i> Native .NET foundation</div>
        <div class="card-body">
          CodeAlta stays native to C#/.NET and keeps the runtime and dependency surface easy to understand, audit, and control.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-life-preserver lunet-feature-icon lunet-icon--controls"></i> Actionable errors</div>
        <div class="card-body">
          CodeAlta turns setup and runtime failures into visible repair paths.
        </div>
      </div>
    </div>
    <div class="col">
      <div class="card h-100">
        <div class="card-header h4"><i class="bi bi-puzzle lunet-feature-icon lunet-icon--data"></i> Plugin support</div>
        <div class="card-body">
          CodeAlta supports trusted local plugins that remain visible as source and manageable from the UI.
        </div>
      </div>
    </div>
  </div>
  <div class="card screenshot-placeholder-card mt-4">
    <div class="card-body text-center py-5">
      <i class="bi bi-images display-5 d-block mb-3 text-secondary"></i>
      <h3 class="h4">Screenshot placeholders</h3>
      <p class="mb-3 text-secondary">Upcoming screenshots will show the main workspace, timeline cards, provider dialogs, thread delegation, theme selection, and plugin management.</p>
      <a href="{{site.basepath}}/docs/principles/" class="btn btn-outline-primary">Read the full principles manifesto</a>
    </div>
  </div>
</section>

<style>
.codealta-ascii-logo {
  display: block;
  width: max-content;
  max-width: 100%;
  overflow-x: auto;
  padding: 1rem 1.25rem;
  margin-bottom: 0;
  border-radius: 1rem;
  background: radial-gradient(circle at 20% 10%, rgba(0, 209, 255, 0.14), transparent 28%), linear-gradient(135deg, rgba(11, 18, 32, 0.52), rgba(17, 27, 48, 0.32));
  border: 1px solid rgba(255, 255, 255, 0.10);
  box-shadow: 0 1.25rem 3rem rgba(0, 0, 0, 0.28);
  color: rgba(234, 242, 255, 0.92);
  font-family: "Cascadia Mono", "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  font-size: clamp(0.42rem, 1.23vw, 1rem);
  line-height: 1.05;
  text-align: left;
  white-space: pre;
}
.logo-code { color: rgba(234, 242, 255, 0.86); }
.logo-alta {
  color: transparent;
  background-image: linear-gradient(115deg, rgba(0, 209, 255, 0.82), #7ae8ff, #4f46e5, #a855f7, #ffffff, #00d1ff);
  background-size: 240% 240%;
  background-position: var(--alta-logo-shift, 0%) 50%;
  -webkit-background-clip: text;
  background-clip: text;
  filter: drop-shadow(0 0 0.4rem rgba(0, 209, 255, 0.26));
}
.terminal-demo-placeholder {
  border-radius: 0.8rem;
  border: 1px solid rgba(255,255,255,.12);
  background: #07111f;
  box-shadow: inset 0 0 0 1px rgba(255,255,255,.03);
  overflow: hidden;
}
.demo-titlebar {
  display: flex;
  align-items: center;
  gap: .45rem;
  padding: .65rem .85rem;
  background: rgba(255,255,255,.06);
  color: rgba(255,255,255,.72);
  font-family: "Cascadia Mono", Consolas, monospace;
  font-size: .85rem;
}
.demo-titlebar span { width: .7rem; height: .7rem; border-radius: 50%; display: inline-block; }
.demo-titlebar span:nth-child(1) { background: #ff5f56; }
.demo-titlebar span:nth-child(2) { background: #ffbd2e; }
.demo-titlebar span:nth-child(3) { background: #27c93f; margin-right: .45rem; }
.terminal-demo-placeholder pre {
  margin: 0;
  padding: 1rem;
  color: #d9e6ff;
  background: transparent;
  white-space: pre-wrap;
}
.screenshot-placeholder-card {
  border-style: dashed;
  background: linear-gradient(135deg, rgba(0, 209, 255, 0.06), rgba(168, 85, 247, 0.05));
}
@media (prefers-reduced-motion: reduce) {
  .logo-alta { background-position: 50% 50%; }
}
</style>

<script>
(function () {
  "use strict";
  var alta = document.querySelector(".logo-alta");
  if (!alta || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  var start;
  function tick(timestamp) {
    if (start === undefined) start = timestamp;
    var phase = ((timestamp - start) / 5200) % 1;
    document.documentElement.style.setProperty("--alta-logo-shift", (phase * 100).toFixed(2) + "%");
    window.requestAnimationFrame(tick);
  }
  window.requestAnimationFrame(tick);
})();
</script>
