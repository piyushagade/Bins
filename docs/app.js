/* ==========================================================================
   Bins for Windows 11 — Interactive Landing Page Logic
   ========================================================================== */

(() => {
  "use strict";

  // --------------------------------------------------------------------------
  // Theme Toggle (Dark / Light)
  // --------------------------------------------------------------------------
  const root = document.documentElement;
  const themeToggle = document.getElementById("themeToggle");
  const savedTheme = localStorage.getItem("bins-site-theme");
  if (savedTheme === "light" || savedTheme === "dark") {
    root.setAttribute("data-theme", savedTheme);
  }

  themeToggle?.addEventListener("click", () => {
    const next = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
    root.setAttribute("data-theme", next);
    localStorage.setItem("bins-site-theme", next);
  });

  // --------------------------------------------------------------------------
  // Interactive Windows 11 Taskbar & Flyout Data
  // --------------------------------------------------------------------------
  const BINS_DATA = {
    dev: {
      name: "Dev",
      accent: "#60CDFF",
      badge: "3 running",
      tabs: {
        Apps: [
          { name: "VS Code", glyph: "VS", color: "#007ACC", running: true, windows: 2 },
          { name: "Terminal", glyph: ">_", color: "#1E293B", running: true, windows: 1 },
          { name: "GitHub", glyph: "GH", color: "#334155", running: true, windows: 1 },
          { name: "Docker", glyph: "DK", color: "#0EA5E9" },
          { name: "Postman", glyph: "PM", color: "#F97316" },
          { name: "Figma", glyph: "FG", color: "#A855F7" },
          { name: "Cloud →", glyph: "☁", color: "#2563EB", subBin: "cloud" },
          { name: "Snippets", glyph: "📋", color: "#10B981", snippet: "dotnet build -c Release" }
        ],
        Snippets: [
          { name: "Build App", glyph: "⚡", color: "#10B981", snippet: ".\\build.ps1 -Run" },
          { name: "Git Status", glyph: "GS", color: "#6366F1", snippet: "git status -sb" },
          { name: "Docker Up", glyph: "DK", color: "#0EA5E9", snippet: "docker compose up -d" },
          { name: "Switch Dev", glyph: "PR", color: "#F59E0B", snippet: "Bins.exe --profile dev" }
        ]
      }
    },
    work: {
      name: "Work",
      accent: "#3B82F6",
      badge: "15 unread",
      tabs: {
        Apps: [
          { name: "Slack", glyph: "SL", color: "#4A154B", running: true, windows: 2, unread: 3 },
          { name: "Outlook", glyph: "OL", color: "#0078D4", running: true, windows: 1, unread: 12 },
          { name: "Notion", glyph: "NT", color: "#1F2937", running: true, windows: 1 },
          { name: "Linear", glyph: "LN", color: "#5E6AD2" },
          { name: "Excel", glyph: "XL", color: "#16A34A" },
          { name: "Teams", glyph: "TM", color: "#4F46E5" },
          { name: "Calendar", glyph: "CA", color: "#0284C7" },
          { name: "Zoom", glyph: "ZM", color: "#2563EB" }
        ],
        Recent: [
          { name: "Q4-Spec.pdf", glyph: "PDF", color: "#E11D48" },
          { name: "Roadmap.xlsx", glyph: "XLS", color: "#16A34A" },
          { name: "Deck-v2.pptx", glyph: "PPT", color: "#EA580C" },
          { name: "Notes.md", glyph: "MD", color: "#475569" }
        ]
      }
    },
    folders: {
      name: "Live Folders",
      accent: "#F59E0B",
      badge: "Smart Live",
      tabs: {
        Downloads: [
          { name: "Bins-1.5.13.exe", glyph: "EXE", color: "#0EA5E9" },
          { name: "Invoice.pdf", glyph: "PDF", color: "#E11D48" },
          { name: "Assets.zip", glyph: "ZIP", color: "#D97706" },
          { name: "Mockup.png", glyph: "PNG", color: "#8B5CF6" },
          { name: "Report.docx", glyph: "DOC", color: "#2563EB" },
          { name: "Schema.json", glyph: "{ }", color: "#10B981" },
          { name: "Desktop →", glyph: "📁", color: "#F59E0B", subBin: "desktop" },
          { name: "Open Dir", glyph: "↗", color: "#475569" }
        ]
      }
    },
    utilities: {
      name: "Utilities",
      accent: "#A855F7",
      badge: "System Tiles",
      tabs: {
        System: [
          { name: "Recycle Bin", glyph: "🗑", color: "#475569" },
          { name: "Screen Snip", glyph: "✂", color: "#0EA5E9" },
          { name: "Audio Out", glyph: "🔊", color: "#8B5CF6" },
          { name: "Dark / Light", glyph: "🌓", color: "#6366F1", action: "toggleTheme" },
          { name: "Play / Pause", glyph: "⏯", color: "#10B981" },
          { name: "Task Mgr", glyph: "📊", color: "#2563EB" },
          { name: "Clear Clip", glyph: "🧹", color: "#F59E0B" },
          { name: "Settings", glyph: "⚙", color: "#334155" }
        ]
      }
    }
  };

  const SUB_BINS = {
    cloud: {
      parent: "dev",
      name: "Dev / Cloud",
      accent: "#38BDF8",
      badge: "Sub-Bin",
      items: [
        { name: "AWS Console", glyph: "AW", color: "#F59E0B" },
        { name: "Netlify", glyph: "NF", color: "#06B6D4" },
        { name: "Cloudflare", glyph: "CF", color: "#F97316" },
        { name: "Azure", glyph: "AZ", color: "#0078D4" }
      ]
    },
    desktop: {
      parent: "folders",
      name: "Folders / Desktop",
      accent: "#F59E0B",
      badge: "Sub-Bin",
      items: [
        { name: "Projects", glyph: "📁", color: "#3B82F6" },
        { name: "Screenshots", glyph: "🖼", color: "#A855F7" },
        { name: "Scratch.txt", glyph: "TXT", color: "#64748B" },
        { name: "Archive.7z", glyph: "7Z", color: "#D97706" }
      ]
    }
  };

  const stage = document.getElementById("win11Stage");
  const flyoutTitle = document.getElementById("flyoutTitle");
  const flyoutDot = document.getElementById("flyoutDot");
  const flyoutBadge = document.getElementById("flyoutBadge");
  const flyoutTabs = document.getElementById("flyoutTabs");
  const flyoutGrid = document.getElementById("flyoutGrid");
  const flyoutBack = document.getElementById("flyoutBack");
  const flyoutLaunchAll = document.getElementById("flyoutLaunchAll");
  const stageToast = document.getElementById("stageToast");

  let activeBinKey = "dev";
  let activeTabName = "Apps";
  let activeSubBinKey = null;
  let toastTimer = null;

  function showStageToast(message) {
    if (!stageToast) return;
    stageToast.textContent = message;
    stageToast.classList.add("show");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => stageToast.classList.remove("show"), 2200);
  }

  function renderFlyout() {
    if (!flyoutGrid || !flyoutTabs) return;

    if (activeSubBinKey && SUB_BINS[activeSubBinKey]) {
      const sub = SUB_BINS[activeSubBinKey];
      flyoutBack.hidden = false;
      flyoutTitle.textContent = sub.name;
      flyoutDot.style.background = sub.accent;
      flyoutDot.style.boxShadow = `0 0 10px ${sub.accent}`;
      flyoutBadge.textContent = sub.badge;
      flyoutTabs.innerHTML = "";
      renderTiles(sub.items);
      return;
    }

    const bin = BINS_DATA[activeBinKey];
    if (!bin) return;

    flyoutBack.hidden = true;
    flyoutTitle.textContent = bin.name;
    flyoutDot.style.background = bin.accent;
    flyoutDot.style.boxShadow = `0 0 10px ${bin.accent}`;
    flyoutBadge.textContent = bin.badge;

    const tabNames = Object.keys(bin.tabs);
    if (!bin.tabs[activeTabName]) {
      activeTabName = tabNames[0];
    }

    flyoutTabs.innerHTML = tabNames
      .map(
        (t) =>
          `<button type="button" class="flyout-tab${t === activeTabName ? " active" : ""}" data-tab="${t}" role="tab" aria-selected="${t === activeTabName}">${t}</button>`
      )
      .join("");

    flyoutTabs.querySelectorAll(".flyout-tab").forEach((btn) => {
      btn.addEventListener("click", () => {
        activeTabName = btn.dataset.tab;
        renderFlyout();
      });
    });

    renderTiles(bin.tabs[activeTabName]);
  }

  function renderTiles(items) {
    flyoutGrid.innerHTML = items
      .map((item, idx) => {
        const runningClass = item.running ? " running" : "";
        const unreadHtml = item.unread ? `<span class="tile-unread">${item.unread}</span>` : "";
        const countHtml = item.windows && item.windows > 1 ? `<span class="tile-count">${item.windows}</span>` : "";
        const subHtml = item.subBin ? `<span class="tile-sub-arrow">→</span>` : "";
        return `
          <button type="button" class="flyout-tile${runningClass}" data-index="${idx}">
            ${countHtml}
            ${unreadHtml}
            <span class="tile-glyph" style="--tile-bg:${item.color}">${item.glyph}</span>
            <span class="tile-name">${item.name}</span>
            ${subHtml}
          </button>
        `;
      })
      .join("");

    flyoutGrid.querySelectorAll(".flyout-tile").forEach((btn) => {
      btn.addEventListener("click", () => {
        const item = items[Number(btn.dataset.index)];
        if (!item) return;

        if (item.subBin) {
          activeSubBinKey = item.subBin;
          renderFlyout();
          return;
        }
        if (item.action === "toggleTheme") {
          themeToggle?.click();
          showStageToast("Toggled Dark / Light theme");
          return;
        }
        if (item.snippet) {
          navigator.clipboard?.writeText(item.snippet).catch(() => {});
          showStageToast(`Copied snippet: ${item.snippet}`);
          return;
        }
        if (item.running) {
          showStageToast(`Focused ${item.name}`);
        } else {
          showStageToast(`Launched ${item.name}`);
        }
      });
    });
  }

  // Taskbar Bin Buttons
  document.querySelectorAll(".tb-bin").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".tb-bin").forEach((b) => b.classList.remove("active"));
      btn.classList.add("active");
      activeBinKey = btn.dataset.bin;
      activeSubBinKey = null;
      activeTabName = Object.keys(BINS_DATA[activeBinKey].tabs)[0];
      renderFlyout();
    });
  });

  flyoutBack?.addEventListener("click", () => {
    activeSubBinKey = null;
    renderFlyout();
  });

  flyoutLaunchAll?.addEventListener("click", () => {
    const name = BINS_DATA[activeBinKey]?.name || "Bin";
    showStageToast(`Launching ${name} workspace with staggered delays…`);
  });

  // Material Switcher (Acrylic / Mica / Solid)
  document.querySelectorAll('[data-control="material"] .seg-btn').forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll('[data-control="material"] .seg-btn').forEach((b) => b.classList.remove("active"));
      btn.classList.add("active");
      stage?.setAttribute("data-material", btn.dataset.value);
      showStageToast(`Flyout backdrop: ${btn.textContent}`);
    });
  });

  // --------------------------------------------------------------------------
  // Global Omni-Search Preview Modal
  // --------------------------------------------------------------------------
  const omniOverlay = document.getElementById("omniOverlay");
  const omniTrigger = document.getElementById("omniTrigger");
  const omniClose = document.getElementById("omniClose");
  const omniInput = document.getElementById("omniInput");
  const omniResults = document.getElementById("omniResults");

  const ALL_ITEMS = Object.entries(BINS_DATA).flatMap(([key, bin]) =>
    Object.values(bin.tabs).flatMap((list) =>
      list.map((item) => ({ ...item, binName: bin.name, binKey: key }))
    )
  );

  function renderOmniResults(query = "") {
    if (!omniResults) return;
    const q = query.trim().toLowerCase();
    const matches = ALL_ITEMS.filter(
      (item) =>
        item.name.toLowerCase().includes(q) ||
        item.binName.toLowerCase().includes(q)
    ).slice(0, 6);

    if (!matches.length) {
      omniResults.innerHTML = `<div style="padding:14px;text-align:center;color:#8e9bb0;font-size:0.82rem;">No matching tiles across your Bins</div>`;
      return;
    }

    omniResults.innerHTML = matches
      .map(
        (item, i) => `
          <button type="button" class="omni-row" data-omni-idx="${i}">
            <span class="omni-row-left">
              <span class="omni-badge-icon" style="--c:${item.color}">${item.glyph}</span>
              <span class="omni-row-name">${item.name}</span>
            </span>
            <span class="omni-row-bin">${item.binName}</span>
          </button>
        `
      )
      .join("");

    omniResults.querySelectorAll(".omni-row").forEach((row) => {
      row.addEventListener("click", () => {
        const picked = matches[Number(row.dataset.omniIdx)];
        closeOmni();
        if (picked) {
          showStageToast(`Omni-Search launched ${picked.name} (${picked.binName})`);
        }
      });
    });
  }

  function openOmni() {
    if (!omniOverlay) return;
    omniOverlay.classList.add("open");
    omniOverlay.setAttribute("aria-hidden", "false");
    if (omniInput) {
      omniInput.value = "";
      renderOmniResults("");
      setTimeout(() => omniInput.focus(), 60);
    }
  }

  function closeOmni() {
    if (!omniOverlay) return;
    omniOverlay.classList.remove("open");
    omniOverlay.setAttribute("aria-hidden", "true");
  }

  omniTrigger?.addEventListener("click", openOmni);
  omniClose?.addEventListener("click", closeOmni);
  omniOverlay?.addEventListener("click", (e) => {
    if (e.target === omniOverlay) closeOmni();
  });
  omniInput?.addEventListener("input", () => renderOmniResults(omniInput.value));

  document.addEventListener("keydown", (e) => {
    if (e.ctrlKey && e.altKey && e.code === "Space") {
      e.preventDefault();
      omniOverlay?.classList.contains("open") ? closeOmni() : openOmni();
    } else if (e.key === "Escape" && omniOverlay?.classList.contains("open")) {
      closeOmni();
    }
  });

  // --------------------------------------------------------------------------
  // Icon Accent Tint Swatches
  // --------------------------------------------------------------------------
  document.querySelectorAll(".tint-dots .swatch").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".tint-dots .swatch").forEach((b) => b.classList.remove("active"));
      btn.classList.add("active");
      root.style.setProperty("--showcase-tint", btn.dataset.tint);
    });
  });

  // --------------------------------------------------------------------------
  // Silent Install Copy Pill
  // --------------------------------------------------------------------------
  const copySilentBtn = document.getElementById("copySilentBtn");
  const copySilentLabel = document.getElementById("copySilentLabel");

  copySilentBtn?.addEventListener("click", () => {
    navigator.clipboard
      ?.writeText(".\\Bins-Setup-1.5.13-x64.exe /SILENT /NORESTART")
      .then(() => {
        if (copySilentLabel) copySilentLabel.textContent = "Copied";
        setTimeout(() => {
          if (copySilentLabel) copySilentLabel.textContent = "Copy";
        }, 1600);
      })
      .catch(() => {});
  });

  // --------------------------------------------------------------------------
  // Taskbar Clock
  // --------------------------------------------------------------------------
  const trayTime = document.getElementById("trayTime");
  function updateClock() {
    if (!trayTime) return;
    const now = new Date();
    trayTime.textContent = now.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  }
  updateClock();
  setInterval(updateClock, 30000);

  // --------------------------------------------------------------------------
  // Scroll Reveal Observer
  // --------------------------------------------------------------------------
  const reveals = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window) {
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("visible");
            io.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12 }
    );
    reveals.forEach((el) => io.observe(el));
  } else {
    reveals.forEach((el) => el.classList.add("visible"));
  }

  // --------------------------------------------------------------------------
  // Live Download Counter (app.piyushagade.xyz/counter)
  // --------------------------------------------------------------------------
  const COUNTER_BASE = "https://app.piyushagade.xyz/counter/bins/downloads";
  const COUNTER_TIMEOUT_MS = 4000;
  const heroDlWrap = document.getElementById("heroDlWrap");
  const heroDlCount = document.getElementById("heroDlCount");
  const heroDlWord = document.getElementById("heroDlWord");
  const boxDlWrap = document.getElementById("boxDlWrap");
  const boxDlCount = document.getElementById("boxDlCount");
  const boxDlWord = document.getElementById("boxDlWord");

  let counterApiUnavailable = false;

  function fetchCounterJson(url, options = {}) {
    if (counterApiUnavailable || typeof fetch !== "function") {
      return Promise.resolve(null);
    }

    const controller = typeof AbortController === "function" ? new AbortController() : null;
    const timer = controller
      ? setTimeout(() => controller.abort(), COUNTER_TIMEOUT_MS)
      : null;

    const fetchOpts = controller ? { ...options, signal: controller.signal } : options;

    return fetch(url, fetchOpts)
      .then((r) => {
        if (!r || !r.ok) {
          counterApiUnavailable = true;
          return null;
        }
        return r.json().catch(() => null);
      })
      .catch(() => {
        counterApiUnavailable = true;
        return null;
      })
      .finally(() => {
        if (timer) clearTimeout(timer);
      });
  }

  function renderDownloadCount(count) {
    if (typeof count !== "number" || !Number.isFinite(count) || count <= 0) return;
    const formatted = count.toLocaleString();
    const word = count === 1 ? "download" : "downloads";
    if (heroDlCount) heroDlCount.textContent = formatted;
    if (heroDlWord) heroDlWord.textContent = word;
    if (heroDlWrap) heroDlWrap.hidden = false;
    if (boxDlCount) boxDlCount.textContent = formatted;
    if (boxDlWord) boxDlWord.textContent = word;
    if (boxDlWrap) boxDlWrap.hidden = false;
  }

  fetchCounterJson(COUNTER_BASE, { cache: "no-store" }).then((data) => {
    if (data && typeof data.count === "number") {
      renderDownloadCount(data.count);
    }
  });

  let dlIncrementInFlight = false;
  document.querySelectorAll("[data-track-download]").forEach((link) => {
    link.addEventListener("click", () => {
      const sessionKey = "bins-dl-counted-v2";
      if (counterApiUnavailable || dlIncrementInFlight || sessionStorage.getItem(sessionKey)) return;
      dlIncrementInFlight = true;

      fetchCounterJson(`${COUNTER_BASE}/up`, { method: "POST", keepalive: true })
        .then((data) => {
          if (data && typeof data.count === "number") {
            sessionStorage.setItem(sessionKey, "1");
            renderDownloadCount(data.count);
          }
        })
        .finally(() => {
          dlIncrementInFlight = false;
        });
    });
  });

  // --------------------------------------------------------------------------
  // Live VirusTotal Scan Badge (from latest GitHub Release notes)
  // --------------------------------------------------------------------------
  const VT_CACHE_KEY = "bins-vt-latest-v1";
  const VT_CACHE_TTL_MS = 10 * 60 * 1000;
  const heroVtWrap = document.getElementById("heroVtWrap");
  const heroVtLink = document.getElementById("heroVtLink");
  const heroVtScore = document.getElementById("heroVtScore");
  const vtPill = document.getElementById("vtPill");
  const vtDot = document.getElementById("vtDot");
  const vtText = document.getElementById("vtText");
  const footerVtLink = document.getElementById("footerVtLink");

  function renderVirusTotal(vt) {
    if (!vt || !vt.url || !vt.summary) return;
    const scoreMatch = vt.summary.match(/^(\d+)\s*\/\s*(\d+)/);
    const shortLabel = scoreMatch ? `${scoreMatch[1]}/${scoreMatch[2]} detections` : vt.summary;
    const heroLabel = scoreMatch ? `${scoreMatch[1]}/${scoreMatch[2]}` : "Verified";
    const flagged = scoreMatch ? parseInt(scoreMatch[1], 10) > 0 : false;

    if (vtPill) {
      vtPill.href = vt.url;
      vtPill.title = `VirusTotal scan (${vt.tag || "latest"}): ${vt.summary}`;
      vtPill.hidden = false;
    }
    if (vtText) vtText.textContent = shortLabel;
    if (vtDot) vtDot.classList.toggle("warn", flagged);

    if (heroVtLink) {
      heroVtLink.href = vt.url;
      heroVtLink.title = `VirusTotal scan: ${vt.summary}`;
    }
    if (heroVtScore) heroVtScore.textContent = heroLabel;
    if (heroVtWrap) heroVtWrap.hidden = false;

    if (footerVtLink) footerVtLink.href = vt.url;
  }

  (function loadVirusTotalBadge() {
    if (typeof fetch !== "function") return;
    try {
      const cachedRaw = sessionStorage.getItem(VT_CACHE_KEY);
      if (cachedRaw) {
        const cached = JSON.parse(cachedRaw);
        if (cached && Date.now() - cached.ts < VT_CACHE_TTL_MS && cached.data) {
          renderVirusTotal(cached.data);
          return;
        }
      }
    } catch (_) {}

    const controller = typeof AbortController === "function" ? new AbortController() : null;
    const timer = controller ? setTimeout(() => controller.abort(), 5000) : null;
    fetch("https://api.github.com/repos/piyushagade/Bins/releases/latest", {
      headers: { Accept: "application/vnd.github+json" },
      ...(controller ? { signal: controller.signal } : {})
    })
      .then((r) => (r && r.ok ? r.json() : null))
      .then((rel) => {
        if (!rel || typeof rel.body !== "string") return;
        const tableMatch =
          rel.body.match(/\|\s*`Bins-Setup-[^`]+`\s*\|\s*`[a-f0-9]+`\s*\|\s*([^|]+?)\s*\|\s*\[View Report\]\((https:\/\/www\.virustotal\.com\/gui\/file\/[a-f0-9]+)\)\s*\|/i) ||
          rel.body.match(/\*\*VirusTotal[^*]*:\*\*\s*\[([^\]]+)\]\((https:\/\/www\.virustotal\.com\/gui\/file\/[a-f0-9]+)\)/i);
        if (!tableMatch) return;
        const data = { summary: tableMatch[1].trim(), url: tableMatch[2], tag: rel.tag_name || "" };
        try {
          sessionStorage.setItem(VT_CACHE_KEY, JSON.stringify({ ts: Date.now(), data }));
        } catch (_) {}
        renderVirusTotal(data);
      })
      .catch(() => {})
      .finally(() => {
        if (timer) clearTimeout(timer);
      });
  })();

  // Initial render
  renderFlyout();
})();
