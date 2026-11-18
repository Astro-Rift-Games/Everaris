(function () {
  "use strict";

  const STORAGE_KEY = "everaris_sfx_guide_done_v1";
  const data = window.SFX_GUIDE_DATA;
  if (!data) return;

  const state = {
    story: "all",
    media: "all",
    query: "",
    doneIds: loadDoneSet(),
    mediaStatus: new Map()
  };

  const els = {
    sidebarStats: document.getElementById("sidebarStats"),
    taskNav: document.getElementById("taskNav"),
    storiesStrip: document.getElementById("storiesStrip"),
    sectionsContainer: document.getElementById("sectionsContainer"),
    searchInput: document.getElementById("searchInput"),
    lightbox: document.getElementById("lightbox"),
    lightboxBackdrop: document.getElementById("lightboxBackdrop"),
    lightboxClose: document.getElementById("lightboxClose"),
    lightboxImage: document.getElementById("lightboxImage"),
    lightboxTitle: document.getElementById("lightboxTitle"),
    lightboxMeta: document.getElementById("lightboxMeta")
  };

  function loadDoneSet() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? new Set(JSON.parse(raw)) : new Set();
    } catch {
      return new Set();
    }
  }

  function saveDoneSet() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify([...state.doneIds]));
    } catch {
      // Ignore storage failures in restricted contexts.
    }
  }

  function escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
  }

  function statusPill(status) {
    if (status === "gdd") {
      return '<span class="pill pill-amber">GDD / Pendiente en Unity</span>';
    }
    if (status === "mixed") {
      return '<span class="pill pill-teal">Parcial Unity + GDD Doc 20</span>';
    }
    return '<span class="pill pill-green">Implementado en proyecto</span>';
  }

  function renderStories() {
    const visibleStories = data.stories.filter(
      (story) => state.story === "all" || story.id === state.story
    );

    els.storiesStrip.innerHTML = visibleStories
      .map(
        (story) => `
      <article class="story-card">
        <div class="story-header">
          <span class="story-badge">${escapeHtml(story.board)} · #${story.hacknplanId}</span>
        </div>
        <h2>${escapeHtml(story.title)}</h2>
        <p>${escapeHtml(story.objective)}</p>
        <ul class="story-notes">
          ${story.tone.map((line) => `<li>${escapeHtml(line)}</li>`).join("")}
        </ul>
      </article>
    `
      )
      .join("");
  }

  function renderNav() {
    const us85 = data.tasks.filter((t) => t.storyId === "US-85");
    const us86 = data.tasks.filter((t) => t.storyId === "US-86");

    const groupHtml = (label, list) => `
      <div class="nav-group-label">${escapeHtml(label)}</div>
      ${list
        .map(
          (t) => `
        <a class="nav-link" href="#task-${t.id}">
          <span><span class="nav-link-code">${escapeHtml(t.id)}</span> · #${t.hacknplanId}</span>
          <span class="nav-count">${t.items.length} bloques</span>
        </a>
      `
        )
        .join("")}
    `;

    els.taskNav.innerHTML = groupHtml("US-85", us85) + groupHtml("US-86", us86);
  }

  function itemMatches(task, item) {
    if (state.story !== "all" && task.storyId !== state.story) {
      return false;
    }

    const loadedState = state.mediaStatus.get(item.id);
    if (state.media === "loaded" && loadedState !== "loaded") return false;
    if (state.media === "missing" && loadedState === "loaded") return false;

    const q = state.query.trim().toLowerCase();
    if (!q) return true;

    const haystack = [
      task.id,
      task.title,
      item.title,
      item.gif,
      item.note,
      ...item.badges,
      ...item.clips
    ]
      .join(" ")
      .toLowerCase();

    return haystack.includes(q);
  }

  function updateStats() {
    const allItems = data.tasks.flatMap((t) => t.items);
    const total = allItems.length;
    const loadedCount = allItems.filter(
      (item) => state.mediaStatus.get(item.id) === "loaded"
    ).length;
    const doneCount = allItems.filter((item) => state.doneIds.has(item.id)).length;

    els.sidebarStats.innerHTML = `
      <div class="stat-item"><strong>${total}</strong><span>Bloques</span></div>
      <div class="stat-item"><strong>${loadedCount}</strong><span>Con GIF</span></div>
      <div class="stat-item"><strong>${doneCount}</strong><span>Listos</span></div>
    `;
  }

  function renderSections() {
    const renderedTasks = data.tasks
      .map((task) => {
        const items = task.items.filter((item) => itemMatches(task, item));
        if (items.length === 0) return "";

        return `
        <section class="task-section" id="task-${task.id}">
          <div class="task-header">
            <div class="task-meta-row">
              <span class="pill pill-gold">${escapeHtml(task.id)} · HacknPlan #${task.hacknplanId}</span>
              <span class="pill pill-teal">${escapeHtml(task.storyId)}</span>
              <span class="pill">Prioridad: ${escapeHtml(task.priority)}</span>
            </div>
            <h2>${escapeHtml(task.title)}</h2>
            <div class="task-summary-grid">
              <div class="task-box">
                <strong>Objetivo</strong>
                <span>${escapeHtml(task.objective)}</span>
              </div>
              <div class="task-box">
                <strong>Tono y estética</strong>
                <ul>${task.tone.map((line) => `<li>${escapeHtml(line)}</li>`).join("")}</ul>
              </div>
              <div class="task-box">
                <strong>Entrega</strong>
                <span>${escapeHtml(task.delivery)}</span>
              </div>
            </div>
          </div>

          <div class="cards-grid">
            ${items.map((item) => renderItemCard(item)).join("")}
          </div>
        </section>
      `;
      })
      .filter(Boolean);

    els.sectionsContainer.innerHTML =
      renderedTasks.length > 0
        ? renderedTasks.join("")
        : '<div class="empty-state">No hay bloques que coincidan con los filtros actuales.</div>';

    bindCardEvents();
  }

  function renderItemCard(item) {
    const isDone = state.doneIds.has(item.id);
    const knownState = state.mediaStatus.get(item.id);
    const mediaClass = knownState === "loaded" ? "cue-media has-media" : "cue-media";
    const imgSrc = `gifs/${encodeURIComponent(item.gif)}`;

    return `
      <article class="cue-card ${isDone ? "is-done" : ""}" data-item-id="${escapeHtml(item.id)}">
        <div class="${mediaClass}">
          <span class="media-status-dot">${knownState === "loaded" ? "GIF cargado" : "Falta GIF"}</span>
          <img
            src="${imgSrc}"
            alt="${escapeHtml(item.title)}"
            loading="eager"
            data-item-id="${escapeHtml(item.id)}"
            data-title="${escapeHtml(item.title)}"
            data-gif="${escapeHtml(item.gif)}"
            style="${knownState === "missing" ? "display:none;" : ""}"
          />
          <div class="media-placeholder">
            <span class="placeholder-label">Sin GIF asignado todavía</span>
            <code>gifs/${escapeHtml(item.gif)}</code>
            <button type="button" class="copy-slug-btn" data-copy="gifs/${escapeHtml(item.gif)}">Copiar nombre</button>
          </div>
        </div>

        <div class="cue-body">
          <div class="cue-top">
            <h3>${escapeHtml(item.title)}</h3>
            <label class="done-toggle">
              <input type="checkbox" data-done-id="${escapeHtml(item.id)}" ${isDone ? "checked" : ""} />
              Listo
            </label>
          </div>

          <div class="cue-badges">
            ${statusPill(item.status)}
            ${item.badges.map((b) => `<span class="pill">${escapeHtml(b)}</span>`).join("")}
          </div>

          <ul class="clip-list">
            ${item.clips.map((c) => `<li>${escapeHtml(c)}</li>`).join("")}
          </ul>

          <p class="cue-note">${escapeHtml(item.note)}</p>
        </div>
      </article>
    `;
  }

  function bindCardEvents() {
    els.sectionsContainer.querySelectorAll(".cue-media img").forEach((img) => {
      img.addEventListener("load", () => {
        const id = img.dataset.itemId;
        state.mediaStatus.set(id, "loaded");
        const wrap = img.closest(".cue-media");
        if (wrap) {
          wrap.classList.add("has-media");
          const dot = wrap.querySelector(".media-status-dot");
          if (dot) dot.textContent = "GIF cargado";
        }
        updateStats();
      });

      img.addEventListener("error", () => {
        const id = img.dataset.itemId;
        state.mediaStatus.set(id, "missing");
        img.style.display = "none";
        const wrap = img.closest(".cue-media");
        if (wrap) {
          wrap.classList.remove("has-media");
          const dot = wrap.querySelector(".media-status-dot");
          if (dot) dot.textContent = "Falta GIF";
        }
        updateStats();
        if (state.media === "loaded") {
          renderSections();
        }
      });

      img.addEventListener("click", () => {
        if (state.mediaStatus.get(img.dataset.itemId) !== "loaded") return;
        openLightbox(img.src, img.dataset.title, `Archivo: gifs/${img.dataset.gif}`);
      });
    });

    els.sectionsContainer.querySelectorAll("input[data-done-id]").forEach((cb) => {
      cb.addEventListener("change", () => {
        const id = cb.dataset.doneId;
        if (cb.checked) state.doneIds.add(id);
        else state.doneIds.delete(id);
        saveDoneSet();
        const card = cb.closest(".cue-card");
        if (card) card.classList.toggle("is-done", cb.checked);
        updateStats();
      });
    });

    els.sectionsContainer.querySelectorAll("button[data-copy]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const value = btn.dataset.copy;
        try {
          await navigator.clipboard.writeText(value);
          btn.textContent = "Copiado";
          setTimeout(() => {
            btn.textContent = "Copiar nombre";
          }, 1200);
        } catch {
          btn.textContent = value;
        }
      });
    });
  }

  function openLightbox(src, title, meta) {
    els.lightboxImage.src = src;
    els.lightboxImage.alt = title;
    els.lightboxTitle.textContent = title;
    els.lightboxMeta.textContent = meta;
    els.lightbox.hidden = false;
  }

  function closeLightbox() {
    els.lightbox.hidden = true;
    els.lightboxImage.src = "";
  }

  function bindToolbar() {
    els.searchInput.addEventListener("input", () => {
      state.query = els.searchInput.value;
      renderSections();
    });

    document.querySelectorAll("button[data-story]").forEach((btn) => {
      btn.addEventListener("click", () => {
        document.querySelectorAll("button[data-story]").forEach((b) => b.classList.remove("active"));
        btn.classList.add("active");
        state.story = btn.dataset.story;
        renderStories();
        renderSections();
      });
    });

    document.querySelectorAll("button[data-media]").forEach((btn) => {
      btn.addEventListener("click", () => {
        document.querySelectorAll("button[data-media]").forEach((b) => b.classList.remove("active"));
        btn.classList.add("active");
        state.media = btn.dataset.media;
        renderSections();
      });
    });

    els.lightboxBackdrop.addEventListener("click", closeLightbox);
    els.lightboxClose.addEventListener("click", closeLightbox);
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && !els.lightbox.hidden) closeLightbox();
    });
  }

  renderNav();
  renderStories();
  updateStats();
  renderSections();
  bindToolbar();
})();
