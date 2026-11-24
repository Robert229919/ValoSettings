"use strict";

// ---------------------------------------------------------------------------
// Pont avec l'appli (WebView2). Hors de l'appli (aperçu dans un navigateur),
// on utilise des données de démonstration (voir mockApi en bas du fichier).
// ---------------------------------------------------------------------------
const webview = window.chrome && window.chrome.webview;
const pending = new Map();
let seq = 0;

if (webview) {
  webview.addEventListener("message", (e) => {
    const m = e.data;
    if (!m) return;
    if (m.event) { onEvent(m); return; }
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    m.ok ? p.resolve(m.result) : p.reject(new Error(m.error || "Erreur"));
  });
}

function api(cmd, args = {}) {
  if (!webview) return mockApi(cmd, args);
  return new Promise((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve, reject });
    webview.postMessage({ id, cmd, args });
  });
}

// ---------------------------------------------------------------------------
// Données & utilitaires
// ---------------------------------------------------------------------------
const CATS = [
  { key: "Viseur",      label: "Viseur",             short: "Vis" },
  { key: "Sensibilite", label: "Sensi & manette",    short: "Sen" },
  { key: "Touches",     label: "Touches",            short: "Tou" },
  { key: "Minimap",     label: "Minimap",            short: "Min" },
  { key: "Audio",       label: "Audio",              short: "Aud" },
  { key: "Interface",   label: "Interface & HUD",    short: "Int" },
  { key: "Graphismes",  label: "Graphismes (ce PC)", short: "Gfx" },
];

const state = { status: null, accounts: [], profiles: [], backups: [], logLines: 0 };
let applyLog = null; // <pre> du panneau d'application en cours

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const pad2 = (n) => String(n).padStart(2, "0");
const fmtDate = (iso, withTime) => {
  if (!iso) return "";
  const d = new Date(iso);
  if (isNaN(d)) return "";
  return withTime
    ? d.toLocaleDateString("fr-FR") + " " + d.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" })
    : d.toLocaleDateString("fr-FR");
};
const fmtSensi = (v) => (v == null || isNaN(+v) ? null : String(+(+v).toFixed(4)).replace(".", ","));
const plural = (n, one, many) => `${n} ${n > 1 ? many : one}`;

const store = {
  get(key, fallback) {
    try { const v = JSON.parse(localStorage.getItem(key)); return v ?? fallback; } catch { return fallback; }
  },
  set(key, value) { try { localStorage.setItem(key, JSON.stringify(value)); } catch {} },
};

function loadChecked() {
  const v = store.get("cats", null);
  return new Set(Array.isArray(v) ? v : CATS.map((c) => c.key));
}

// ---------------------------------------------------------------------------
// Déroulé (affiché dans le panneau « Appliquer ») & toasts
// ---------------------------------------------------------------------------
function log(text) {
  if (applyLog) { applyLog.hidden = false; applyLog.textContent += text + "\n"; applyLog.scrollTop = applyLog.scrollHeight; }
}

function onEvent(m) { if (m.event === "log") log(m.text); }

function toast(text, kind = "") {
  const el = document.createElement("div");
  el.className = "toast " + kind;
  el.setAttribute("role", kind === "err" ? "alert" : "status");
  el.textContent = text;
  $("#toasts").appendChild(el);
  setTimeout(() => el.remove(), kind === "err" ? 6500 : 3800);
}

function fail(e) { const msg = (e && e.message) || String(e); log("ERREUR  " + msg); toast(msg, "err"); }

// ---------------------------------------------------------------------------
// Fenêtres : dialogue centré ou panneau latéral pleine hauteur
// ---------------------------------------------------------------------------
function overlay(html, { side = false, label = "", onClose } = {}) {
  const prevFocus = document.activeElement;
  const back = document.createElement("div");
  back.className = "overlay" + (side ? " side" : "");
  back.innerHTML = `<div class="${side ? "panel" : "dialog"}" role="dialog" aria-modal="true" aria-label="${esc(label)}">${html}</div>`;
  const close = () => {
    back.remove();
    document.removeEventListener("keydown", onKey, true);
    onClose && onClose();
    if (prevFocus && prevFocus.focus && document.contains(prevFocus)) prevFocus.focus();
  };
  const onKey = (e) => {
    if (back !== $("#modalRoot").lastElementChild) return;
    if (e.key === "Escape" && !back.dataset.locked) { e.preventDefault(); close(); return; }
    if (e.key === "Tab") { // garde le focus dans la fenêtre
      const f = $$("button, input, textarea, [tabindex='0'], summary", back).filter((x) => !x.disabled && x.offsetParent !== null);
      if (!f.length) return;
      const first = f[0], last = f[f.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }
  };
  back.addEventListener("mousedown", (e) => { if (e.target === back && !back.dataset.locked) close(); });
  document.addEventListener("keydown", onKey, true);
  $("#modalRoot").appendChild(back);
  return { el: back.firstElementChild, back, close };
}

function ask({ title, text = "", value = "", okLabel = "OK", multiline = false, placeholder = "" }) {
  return new Promise((resolve) => {
    let done = false;
    const field = multiline
      ? `<textarea id="f" spellcheck="false" placeholder="${esc(placeholder)}">${esc(value)}</textarea>`
      : `<input type="text" id="f" value="${esc(value)}" placeholder="${esc(placeholder)}" maxlength="80" spellcheck="false">`;
    const m = overlay(`
      <h2>${esc(title)}</h2>
      ${text ? `<p>${esc(text)}</p>` : ""}
      ${field}
      <div class="dialog-actions">
        <button class="btn" data-x>Annuler</button>
        <button class="btn primary" data-ok>[ ${esc(okLabel)} ]</button>
      </div>`, { label: title, onClose: () => { if (!done) resolve(null); } });
    const f = $("#f", m.el);
    f.focus(); if (!multiline) f.select();
    const ok = () => { const v = f.value.trim(); if (!v) { f.focus(); return; } done = true; m.close(); resolve(v); };
    $("[data-ok]", m.el).onclick = ok;
    $("[data-x]", m.el).onclick = () => m.close();
    if (!multiline) f.addEventListener("keydown", (e) => { if (e.key === "Enter") ok(); });
  });
}

function confirmBox({ title, text, okLabel = "Confirmer", danger = false }) {
  return new Promise((resolve) => {
    let done = false;
    const m = overlay(`
      <h2>${esc(title)}</h2>
      <p>${esc(text)}</p>
      <div class="dialog-actions">
        <button class="btn" data-x>Annuler</button>
        <button class="btn primary ${danger ? "danger" : ""}" data-ok>[ ${esc(okLabel)} ]</button>
      </div>`, { label: title, onClose: () => { if (!done) resolve(false); } });
    $("[data-x]", m.el).focus();
    $("[data-ok]", m.el).onclick = () => { done = true; m.close(); resolve(true); };
    $("[data-x]", m.el).onclick = () => m.close();
  });
}

// ---------------------------------------------------------------------------
// Statut du Riot Client (sondé toutes les 3 s)
// ---------------------------------------------------------------------------
async function refreshStatus() {
  let s;
  try { s = await api("status"); } catch (e) { s = { connected: false, message: e.message }; }
  s = s || { connected: false };
  const prev = state.status;
  state.status = s;

  const st = $("#status");
  if (s.connected) {
    st.innerHTML = `<span class="st" title="${esc(s.riotId)}">● Connecté — <span class="id">${esc(s.riotId)}</span></span>`;
  } else {
    const msg = s.message ? ` — ${esc(s.message)}` : "";
    st.innerHTML = `<span class="st off" title="${esc(s.message || "")}">○ Hors ligne${msg}</span>`;
  }
  const alert = $("#alert");
  alert.hidden = !s.valorantRunning;
  alert.textContent = s.valorantRunning ? "▲ Ferme Valorant avant d'appliquer" : "";

  $$('[data-action="save-current"]').forEach((b) => (b.disabled = !s.connected));
  if (!prev || prev.puuid !== s.puuid || prev.connected !== s.connected) loadAccounts();
}

// ---------------------------------------------------------------------------
// Comptes
// ---------------------------------------------------------------------------
async function loadAccounts() {
  try { state.accounts = (await api("accounts")) || []; } catch (e) { fail(e); return; }
  renderAccounts();
}

const accountTitle = (a) => a.label || a.riotId || `Compte ${String(a.puuid).slice(0, 8)}`;

function renderAccounts() {
  const list = [...state.accounts].sort((a, b) =>
    (!!b.connected - !!a.connected) ||
    (new Date(b.lastSeen || b.lastPlayed || 0) - new Date(a.lastSeen || a.lastPlayed || 0)));
  const on = list.filter((a) => a.connected).length;

  const grid = $("#accountsGrid");
  if (!list.length) {
    grid.innerHTML = `<div class="empty"><b>Aucun compte</b>
      <span>Connecte-toi à un compte dans le Riot Client.</span></div>`;
    return;
  }
  const head = `<div class="thead micro"><span>N°</span><span>Compte</span><span>Région</span><span>Dernière partie</span><span class="num">Sensi</span><span class="num">Profils</span></div>`;
  grid.innerHTML = head + list.map((a, i) => {
    const sensi = fmtSensi(a.sensitivity);
    const played = fmtDate(a.lastPlayed || a.lastSeen);
    const sub = a.label && a.riotId ? `<span class="mono">${esc(a.riotId)}</span>` : "";
    return `
      <article class="row" data-puuid="${esc(a.puuid)}">
        <div class="row-main">
          <span class="row-idx">${pad2(i + 1)}</span>
          <div class="row-name">
            <h3>${esc(accountTitle(a))}${a.connected ? `<span class="tag on">Connecté</span>` : ""}</h3>
            ${sub ? `<p class="row-sub">${sub}</p>` : ""}
          </div>
          <span class="cell ${a.region ? "" : "na"}">${esc(a.region || "—")}</span>
          <span class="cell ${played ? "" : "na"}">${esc(played || "—")}</span>
          <span class="cell num ${sensi ? "" : "na"}">${esc(sensi || "—")}</span>
          <span class="cell num">${esc(pad2(a.profileCount || 0))}</span>
        </div>
        <div class="row-actions">
          ${a.connected ? `<button class="btn primary" data-action="save-current">Enregistrer</button>
                           <button class="btn" data-action="goto-profiles">Appliquer →</button>` : ""}
          <button class="btn" data-action="label">Surnom</button>
        </div>
      </article>`;
  }).join("");
  $$('[data-action="save-current"]').forEach((b) => (b.disabled = !(state.status && state.status.connected)));
}

async function editLabel(puuid) {
  const a = state.accounts.find((x) => x.puuid === puuid);
  if (!a) return;
  const v = await ask({
    title: "Surnom",
    value: a.label || "", okLabel: "OK", placeholder: a.riotId || "Main, Duo…",
  });
  if (v == null) return;
  try { await api("setLabel", { puuid, label: v }); await loadAccounts(); toast("Surnom enregistré", "ok"); }
  catch (e) { fail(e); }
}

// ---------------------------------------------------------------------------
// Profils
// ---------------------------------------------------------------------------
async function loadProfiles() {
  try {
    const r = (await api("profiles")) || {};
    state.profiles = r.profiles || [];
    state.backups = r.backups || [];
  } catch (e) { fail(e); return; }
  renderProfiles();
}

const profilesHead = () => `<div class="thead micro"><span>N°</span><span>Profil</span>${
  CATS.map((c) => `<span class="num" title="${esc(c.label)}">${esc(c.short)}</span>`).join("")}</div>`;

function profileRow(p, i) {
  const counts = p.counts || {};
  const cells = CATS.map((c) => {
    const n = counts[c.key] || 0;
    const txt = c.key === "Graphismes" ? (n ? "oui" : "—") : (n ? String(n) : "—");
    return `<span class="cell num ${n ? "" : "na"}" title="${esc(c.label)}">${esc(txt)}</span>`;
  }).join("");
  const left = p.isBackup && p.expiresAt ? Math.max(0, Math.ceil((new Date(p.expiresAt) - Date.now()) / 60000)) : null;
  const sub = [
    p.source && `<span class="mono">${esc(p.source)}</span>`,
    left != null ? `expire dans ${left} min` : esc(fmtDate(p.created)),
  ].filter(Boolean).join(" · ");
  return `
    <article class="row" data-id="${esc(p.id)}">
      <div class="row-main">
        <span class="row-idx">${pad2(i + 1)}</span>
        <div class="row-name">
          <h3>${esc(p.name)}${p.partial ? `<span class="tag" title="Ce profil ne contient qu'une partie des réglages">Partiel</span>` : ""}</h3>
          <p class="row-sub">${sub}</p>
        </div>
        ${cells}
      </div>
      <div class="row-actions">
        <button class="btn primary" data-action="apply">Appliquer →</button>
        ${p.isBackup ? "" : `
          <button class="btn" data-action="export">Exporter</button>
          <button class="btn" data-action="code">Code</button>
          <button class="btn" data-action="rename">Renommer</button>`}
        <button class="btn danger" data-action="delete">Supprimer</button>
      </div>
    </article>`;
}

function renderProfiles() {
  $("#nBackups").textContent = `(${pad2(state.backups.length)})`;
  $("#profilesList").innerHTML = state.profiles.length
    ? profilesHead() + state.profiles.map(profileRow).join("")
    : `<div class="empty"><b>Aucun profil</b><span>Enregistre un compte pour commencer.</span></div>`;
  $("#backupsList").innerHTML = state.backups.length ? profilesHead() + state.backups.map(profileRow).join("") : "";
  $("#backupsBox").hidden = !state.backups.length;
}

const findProfile = (id) => state.profiles.find((p) => p.id === id) || state.backups.find((p) => p.id === id);

// ---------------------------------------------------------------------------
// Panneau « Enregistrer » : choix des réglages
// ---------------------------------------------------------------------------
async function openSave({ expand } = {}) {
  const s = state.status;
  if (!s || !s.connected) { toast("Aucun compte connecté", "err"); return; }

  const m = overlay(`
    <div class="panel-head">
      <p class="micro dim">Nouveau profil</p>
      <button class="btn" data-x>✕</button>
      <h2>Enregistrer <span class="mono" style="font-size:.62em;letter-spacing:0">${esc(s.riotId)}</span></h2>
    </div>
    <div class="panel-body"><p class="loading micro">Lecture des settings…</p></div>
    <div class="panel-foot">
      <span class="sel-count" data-count></span>
      <div class="right">
        <button class="btn" data-cancel>Annuler</button>
        <button class="btn solid" data-ok disabled>Enregistrer →</button>
      </div>
    </div>`, { side: true, label: "Enregistrer un profil" });
  $("[data-x]", m.el).onclick = () => m.close();
  $("[data-cancel]", m.el).onclick = () => m.close();
  $("[data-x]", m.el).focus();

  let data;
  try { data = await api("currentSettings"); }
  catch (e) {
    $(".panel-body", m.el).innerHTML = `<p class="error-box">${esc(e.message)}</p>`;
    log("ERREUR  " + e.message);
    return;
  }
  if (!document.contains(m.el)) return;

  const cats = (data.categories || []).map((c) => ({ ...c, items: c.items || [] }));
  const allIds = cats.flatMap((c) => c.items.map((it) => it.id));
  const excluded = new Set(store.get("saveExcluded", []));
  const selected = new Set(allIds.filter((id) => !excluded.has(id)));

  const body = $(".panel-body", m.el);
  body.innerHTML = `
    <div class="block">
      <label class="field">
        <span class="micro dim">Nom</span>
        <input type="text" id="saveName" maxlength="80" spellcheck="false" value="${esc(data.riotId || s.riotId)}">
      </label>
    </div>
    <div class="block">
      <div class="block-head">
        <span class="micro">Réglages <span class="cnt dim" data-total></span></span>
        <div class="tools"><button class="btn" data-all>Tout</button><button class="btn" data-none>Rien</button></div>
      </div>
      ${cats.map((c, ci) => `
        <div class="cat ${c.items.length ? "" : "empty"}" data-cat="${ci}">
          <div class="cat-row">
            <input type="checkbox" data-catbox aria-label="${esc(c.label)}" ${c.items.length ? "" : "disabled"}>
            <span class="i">${pad2(ci + 1)}</span>
            <button class="t" data-exp aria-expanded="false" ${c.items.length ? "" : "disabled"}>${esc(c.label)} <span class="n" data-n></span></button>
            <span class="mini">${c.items.length ? `<button class="btn" data-cat-all>Tout</button><button class="btn" data-cat-none>Rien</button>` : ""}</span>
            ${c.items.length ? `<button class="btn exp" data-exp aria-label="Déplier ${esc(c.label)}">+</button>` : `<span class="exp"></span>`}
          </div>
          <div class="items" hidden>
            ${c.items.map((it) => `
              <label class="item">
                <input type="checkbox" value="${esc(it.id)}">
                <span class="k" title="${esc(it.name)}">${esc(it.name)}</span>
                <span class="v" title="${esc(it.value)}">${esc(it.value)}</span>
              </label>`).join("")}
          </div>
        </div>`).join("")}
      <p class="hint">Valeurs par défaut non listées.</p>
    </div>`;

  const okBtn = $("[data-ok]", m.el);
  const nameField = $("#saveName", m.el);
  const update = () => {
    $$(".cat", body).forEach((el) => {
      const c = cats[+el.dataset.cat];
      const n = c.items.filter((it) => selected.has(it.id)).length;
      const box = $("[data-catbox]", el);
      box.checked = n > 0 && n === c.items.length;
      box.indeterminate = n > 0 && n < c.items.length;
      $("[data-n]", el).textContent = `(${n}/${c.items.length})`;
      $$(".item input", el).forEach((b) => { b.checked = selected.has(b.value); b.parentElement.classList.toggle("off", !b.checked); });
    });
    $("[data-total]", body).textContent = `(${selected.size}/${allIds.length})`;
    $("[data-count]", m.el).textContent = `${selected.size} / ${allIds.length} réglages`;
    okBtn.disabled = selected.size === 0 || !nameField.value.trim();
  };
  const setMany = (ids, on) => { ids.forEach((id) => (on ? selected.add(id) : selected.delete(id))); update(); };

  $("[data-all]", body).onclick = () => setMany(allIds, true);
  $("[data-none]", body).onclick = () => setMany(allIds, false);
  nameField.addEventListener("input", update);
  nameField.addEventListener("keydown", (e) => { if (e.key === "Enter" && !okBtn.disabled) okBtn.click(); });
  $$(".cat", body).forEach((el) => {
    const c = cats[+el.dataset.cat];
    const ids = c.items.map((it) => it.id);
    const items = $(".items", el);
    $("[data-catbox]", el).onchange = (e) => setMany(ids, e.target.checked);
    const allB = $("[data-cat-all]", el), noneB = $("[data-cat-none]", el);
    if (allB) allB.onclick = () => setMany(ids, true);
    if (noneB) noneB.onclick = () => setMany(ids, false);
    const toggle = () => {
      if (!c.items.length) return;
      items.hidden = !items.hidden;
      $$("[data-exp]", el).forEach((b) => b.setAttribute("aria-expanded", String(!items.hidden)));
      $(".btn.exp", el).textContent = items.hidden ? "+" : "−";
    };
    $$("[data-exp]", el).forEach((b) => (b.onclick = toggle));
    $$(".item input", el).forEach((b) => (b.onchange = () => setMany([b.value], b.checked)));
    if (expand && expand.includes(c.key)) toggle();
  });
  update();
  nameField.focus(); nameField.select();

  okBtn.onclick = async () => {
    const name = nameField.value.trim();
    if (!name || !selected.size) return;
    const include = allIds.filter((id) => selected.has(id));
    // Mémorise ce qui a été décoché (et garde les anciens choix pour les réglages absents ici).
    const present = new Set(allIds);
    const nextExcluded = [...excluded].filter((id) => !present.has(id)).concat(allIds.filter((id) => !selected.has(id)));
    store.set("saveExcluded", nextExcluded);
    okBtn.disabled = true;
    m.back.dataset.locked = "1";
    try {
      log(`Enregistrement de ${data.riotId || s.riotId} (${include.length}/${allIds.length} réglages)…`);
      const p = await api("saveProfile", { name, puuid: data.puuid || s.puuid, include });
      log(`OK  Profil « ${p.name} » enregistré.`);
      toast(`Profil « ${p.name} » enregistré`, "ok");
      delete m.back.dataset.locked;
      m.close();
      await Promise.all([loadProfiles(), loadAccounts()]);
    } catch (e) {
      delete m.back.dataset.locked;
      okBtn.disabled = false;
      fail(e);
    }
  };
}

// ---------------------------------------------------------------------------
// Panneau « Appliquer »
// ---------------------------------------------------------------------------
async function openApply(p) {
  if (!p) return;
  await refreshStatus();
  const s = state.status;
  const checked = loadChecked();
  const blocked = !s.connected ? "Aucun compte connecté"
    : s.valorantRunning ? "Ferme Valorant d'abord" : null;
  const same = s.connected && p.sourcePuuid && s.puuid === p.sourcePuuid;
  const counts = p.counts || {};

  const rows = CATS.map((c, i) => {
    const n = counts[c.key] || 0;
    const off = n === 0;
    return `
      <label class="opt ${off ? "disabled" : ""}">
        <input type="checkbox" value="${esc(c.key)}" ${checked.has(c.key) && !off ? "checked" : ""} ${off ? "disabled" : ""}>
        <span class="i">${pad2(i + 1)}</span>
        <span class="t">${esc(c.label)}</span>
        <span class="n">${c.key === "Graphismes" ? (n ? "oui" : "—") : (n ? `(${n})` : "—")}</span>
      </label>`;
  }).join("");

  const m = overlay(`
    <div class="panel-head">
      <p class="micro dim">${p.isBackup ? "Restaurer" : "Appliquer"}${p.partial ? " · partiel" : ""}</p>
      <button class="btn" data-close>✕</button>
      <h2>${esc(p.name)}</h2>
    </div>
    <div class="panel-body">
      <div class="block">
        <div class="target ${blocked ? "bad" : ""}">
          <span class="micro dim">${blocked ? "Bloqué" : "Sur"}</span>
          <span class="v">${blocked ? "▲ " + esc(blocked) : `${esc(s.riotId)}${same ? "<small>compte d'origine</small>" : ""}`}</span>
        </div>
      </div>
      <div class="block">
        <div class="block-head">
          <span class="micro">Catégories</span>
          <div class="tools"><button class="btn" data-all>Tout</button><button class="btn" data-none>Rien</button></div>
        </div>
        ${rows}
        <p class="hint">Sauvegarde auto avant application.</p>
      </div>
      <pre class="runlog" hidden></pre>
    </div>
    <div class="panel-foot">
      <span class="sel-count" data-count></span>
      <div class="right">
        <button class="btn" data-x>Annuler</button>
        <button class="btn solid" data-ok ${blocked ? "disabled" : ""}>Appliquer →</button>
      </div>
    </div>`, { side: true, label: "Appliquer un profil", onClose: () => { applyLog = null; } });

  const boxes = $$(".opt input:not(:disabled)", m.el);
  const okBtn = $("[data-ok]", m.el), xBtn = $("[data-x]", m.el), closeBtn = $("[data-close]", m.el);
  const sync = () => {
    const sel = boxes.filter((b) => b.checked).length;
    const all = loadChecked();
    boxes.forEach((b) => (b.checked ? all.add(b.value) : all.delete(b.value)));
    store.set("cats", [...all]);
    $("[data-count]", m.el).textContent = `${sel} / ${boxes.length} catégories`;
    okBtn.disabled = !!blocked || sel === 0;
  };
  boxes.forEach((b) => (b.onchange = sync));
  $("[data-all]", m.el).onclick = () => { boxes.forEach((b) => (b.checked = true)); sync(); };
  $("[data-none]", m.el).onclick = () => { boxes.forEach((b) => (b.checked = false)); sync(); };
  xBtn.onclick = () => m.close();
  closeBtn.onclick = () => m.close();
  sync();
  (blocked ? xBtn : okBtn).focus();

  okBtn.onclick = async () => {
    const cats = boxes.filter((b) => b.checked).map((b) => b.value);
    okBtn.disabled = xBtn.disabled = closeBtn.disabled = true;
    boxes.forEach((b) => (b.disabled = true));
    $("[data-all]", m.el).disabled = $("[data-none]", m.el).disabled = true;
    m.back.dataset.locked = "1";
    applyLog = $(".runlog", m.el);
    applyLog.hidden = false;
    log(`Application de « ${p.name} » sur ${s.riotId} (${cats.join(", ")})`);
    try {
      await api("apply", { id: p.id, cats, targetPuuid: s.puuid });
      toast(`« ${p.name} » appliqué sur ${s.riotId}`, "ok");
    } catch (e) { fail(e); }
    delete m.back.dataset.locked;
    xBtn.disabled = closeBtn.disabled = false;
    xBtn.textContent = "Fermer";
    xBtn.classList.add("primary");
    okBtn.hidden = true;
    xBtn.focus();
    loadProfiles(); loadAccounts();
  };
}

// ---------------------------------------------------------------------------
// Autres actions sur les profils
// ---------------------------------------------------------------------------
async function renameProfile(p) {
  const name = await ask({ title: "Renommer le profil", value: p.name, okLabel: "Renommer" });
  if (name == null) return;
  try { await api("renameProfile", { id: p.id, name }); await loadProfiles(); }
  catch (e) { fail(e); }
}

async function deleteProfile(p) {
  const ok = await confirmBox({
    title: p.isBackup ? "Supprimer cette sauvegarde ?" : "Supprimer ce profil ?",
    text: `« ${p.name} » sera supprimé définitivement.`,
    okLabel: "Supprimer", danger: true,
  });
  if (!ok) return;
  try { await api("deleteProfile", { id: p.id }); log(`Profil « ${p.name} » supprimé.`); await Promise.all([loadProfiles(), loadAccounts()]); }
  catch (e) { fail(e); }
}

async function exportProfile(p) {
  try {
    const path = await api("exportProfile", { id: p.id });
    if (path) { log(`Profil « ${p.name} » exporté vers ${path}`); toast("Profil exporté", "ok"); }
  } catch (e) { fail(e); }
}

async function copyCode(p) {
  try {
    const len = await api("copyCode", { id: p.id });
    log(`Code du profil « ${p.name} » copié (${len} caractères).`);
    toast("Code copié", "ok");
  } catch (e) { fail(e); }
}

async function importFile() {
  try {
    const r = await api("importFile");
    if (!r) return;
    const imported = r.imported || [], errors = r.errors || [];
    imported.forEach((n) => log(`OK  Profil importé : ${n}`));
    errors.forEach((e) => log(`ERREUR  ${e}`));
    if (imported.length) toast(plural(imported.length, "profil importé", "profils importés"), "ok");
    if (errors.length) toast(errors[0], "err");
    await loadProfiles();
  } catch (e) { fail(e); }
}

async function importCode() {
  const code = await ask({
    title: "Importer un code",
    multiline: true, okLabel: "Importer", placeholder: "VALOSETTINGS1:…",
  });
  if (code == null) return;
  try {
    const p = await api("importCode", { code });
    log(`OK  Profil importé depuis un code : ${p.name}`);
    toast(`Profil « ${p.name} » importé`, "ok");
    await loadProfiles();
  } catch (e) { fail(e); }
}

// ---------------------------------------------------------------------------
// Navigation & clics
// ---------------------------------------------------------------------------
function showTab(name) {
  if (!$("#tab-" + name)) return;
  $$(".nav-item").forEach((t) => {
    const on = t.dataset.tab === name;
    t.classList.toggle("active", on);
    t.setAttribute("aria-selected", String(on));
  });
  $$(".view").forEach((v) => v.classList.toggle("active", v.id === "tab-" + name));
  $("#main").scrollTop = 0;
}

document.addEventListener("click", (e) => {
  const tab = e.target.closest(".nav-item");
  if (tab) { showTab(tab.dataset.tab); return; }
  const btn = e.target.closest("[data-action]");
  if (!btn || btn.disabled) return;
  const row = btn.closest("[data-id]");
  const p = row && findProfile(row.dataset.id);
  const acc = btn.closest("[data-puuid]");
  switch (btn.dataset.action) {
    case "save-current": openSave(); break;
    case "goto-profiles": showTab("profiles"); break;
    case "label": acc && editLabel(acc.dataset.puuid); break;
    case "import-file": importFile(); break;
    case "import-code": importCode(); break;
    case "open-folder": api("openFolder").catch(fail); break;
    case "apply": p && openApply(p); break;
    case "export": p && exportProfile(p); break;
    case "code": p && copyCode(p); break;
    case "rename": p && renameProfile(p); break;
    case "delete": p && deleteProfile(p); break;
  }
});

// Aperçu hors appli : #accounts, #profiles, #apply, #save
// #code, #delete (dialogues) ; drapeaux : #save&expand, #accounts&offline, #apply&running, #profiles&backups.
function previewRoute() {
  const [h, ...flags] = location.hash.slice(1).split("&");
  if (h === "apply") { showTab("profiles"); openApply(state.profiles[0]); }
  else if (h === "save") { showTab("profiles"); openSave({ expand: flags.includes("expand") ? ["Viseur", "Touches"] : null }); }
  else if (h === "code") { showTab("profiles"); importCode(); }
  else if (h === "delete") { showTab("profiles"); deleteProfile(state.profiles[0]); }
  else if (h) showTab(h);
  if (flags.includes("backups")) $("#backupsBox").open = true;
}

// ---------------------------------------------------------------------------
// Démarrage
// ---------------------------------------------------------------------------
async function checkUpdate() {
  let u;
  try { u = await api("checkUpdate"); } catch { return; }
  if (!u || !u.available || store.get("skipUpdate", "") === u.latest) return;
  let accepted = false;
  const m = overlay(`
    <h2>Nouvelle version disponible</h2>
    <p>La version <b>${esc(u.latest)}</b> de  est sortie (tu utilises la ${esc(u.current)}). Veux-tu mettre à jour ?</p>
    <p>Une page s'ouvre dans ton navigateur : télécharge le nouveau .exe, ferme , puis remplace l'ancien.</p>
    <div class="dialog-actions">
      <button class="btn" data-x>Non</button>
      <button class="btn primary" data-ok>[ Mettre à jour ]</button>
    </div>`, { label: "Nouvelle version", onClose: () => { if (!accepted) store.set("skipUpdate", u.latest); } });
  $("[data-x]", m.el).onclick = () => m.close();
  $("[data-ok]", m.el).onclick = async () => {
    accepted = true; m.close();
    try { await api("openUpdate"); } catch (e) { fail(e); }
  };
  $("[data-ok]", m.el).focus();
}

(async function start() {
  const howto = $("#howto");
  howto.open = store.get("howto", true);
  howto.addEventListener("toggle", () => store.set("howto", howto.open));
  log("ValoSettings prêt.");
  await refreshStatus();
  await loadProfiles();
  checkUpdate();
  setInterval(refreshStatus, 3000);
  // Sauvegardes : compte à rebours et disparition après 1 h.
  setInterval(() => { if (state.backups.length) loadProfiles(); }, 60000);
  if (!webview) {
    mockSeedLog();
    if (location.hash) previewRoute();
  }
})();

// ---------------------------------------------------------------------------
// Données de démonstration (aperçu hors de l'appli uniquement)
// ---------------------------------------------------------------------------
var mock; // var (sans valeur) : start() l'utilise avant cette ligne

function mockSeedLog() {
  ["Riot Client détecté, compte connecté : Demo#0001",
   "4 comptes trouvés dans le dossier Valorant.",
   "Application de « Setup principal » sur Duo#EUW (Viseur, Sensibilite, Touches)",
   "  Sauvegarde du compte cible : Sauvegarde Duo#EUW 2026-11-24 00-03-40",
   "  Viseur : 37 réglages écrits",
   "  Sensi & manette : 7 réglages écrits",
   "  Touches : 30 réglages écrits",
   "OK  Profil appliqué. Relance Valorant pour voir les changements."].forEach(log);
}

function mockSettings() {
  const C = (key, label, pairs) => ({ key, label, items: pairs.map(([name, value]) => ({ id: `${key}.${name}`, name, value })) });
  return [
    C("Viseur", "Viseur", [
      ["CrosshairColor", "#00FFFF"], ["CrosshairOutlineOpacity", "0.5"], ["CrosshairCenterDot", "true"],
      ["CrosshairCenterDotSize", "2"], ["CrosshairInnerLinesLength", "4"], ["CrosshairInnerLinesThickness", "2"],
      ["CrosshairInnerLinesOffset", "2"], ["CrosshairOuterLinesShow", "false"], ["FadeCrosshairWithFiringError", "false"],
      ["ShowSpectatedPlayerCrosshair", "true"], ["SavedCrosshairProfileData", "{\"profiles\":[{\"name\":\"Main\"},{\"name\":\"Op\"},{\"name\":\"Smoke\"}]}"],
    ]),
    C("Sensibilite", "Sensi & manette", [
      ["MouseSensitivity", "0.2"], ["MouseSensitivityADS", "1"], ["MouseSensitivityZoomed", "1"],
      ["GamepadSensitivity", "0.6"], ["InvertMouse", "false"],
    ]),
    C("Touches", "Touches", [
      ["Ability1", "Q"], ["Ability2", "E"], ["Grenade", "C"], ["Ultimate", "X"], ["Jump", "Space Bar"],
      ["Crouch", "Left Ctrl"], ["Walk", "Left Shift"], ["UseObject", "F"], ["Inspect", "Y"],
      ["PushToTalk", "V"], ["Map", "M"], ["Scoreboard", "Tab"], ["EquipPrimary", "1"], ["EquipSecondary", "2"],
    ]),
    C("Minimap", "Minimap", [["MinimapRotate", "true"], ["MinimapKeepCentered", "false"], ["MinimapSize", "1.1"], ["MinimapZoom", "0.9"]]),
    C("Audio", "Audio", [["MasterVolume", "0.6"], ["MusicVolume", "0"], ["VoiceChatVolume", "0.8"], ["SoundEffectsVolume", "1"]]),
    C("Interface", "Interface & HUD", [
      ["ShowCorpses", "false"], ["ShowBlood", "false"], ["ShowMatureContent", "false"], ["HideUserNames", "true"],
      ["ShowFPS", "true"], ["ShowNetworkStats", "Text Only"], ["CycleMatchFeed", "true"], ["MatchTimerVisibility", "1"],
    ]),
    C("Graphismes", "Graphismes (ce PC)", [["GameUserSettings.ini", "2560×1440 · 240 Hz · qualité basse"]]),
  ];
}

function mockInit() {
  const flags = location.hash.slice(1).split("&");
  const counts = { Viseur: 37, Sensibilite: 7, Touches: 30, Minimap: 3, Audio: 4, Interface: 15, Graphismes: 1 };
  const P1 = "00000000-0000-4000-8000-000000000001";
  mock = {
    status: flags.includes("offline")
      ? { connected: false, riotId: null, puuid: null, valorantRunning: false, message: "Riot Client fermé" }
      : { connected: true, riotId: "Demo#0001", puuid: P1, valorantRunning: flags.includes("running"), message: null },
    accounts: [
      { puuid: P1, riotId: "Demo#0001", label: "Main", region: "EU", lastPlayed: "2026-11-23T22:48:07", lastSeen: "2026-11-24T00:01:00", sensitivity: 0.2, profileCount: 2, backupCount: 1, lastProfile: "Setup principal", connected: true },
      { puuid: "00000000-0000-4000-8000-000000000002", riotId: "Duo#EUW", label: "", region: "EU", lastPlayed: "2026-10-17T10:19:21", lastSeen: "2026-10-17T10:19:21", sensitivity: 0.35, profileCount: 1, backupCount: 0, lastProfile: null, connected: false },
      { puuid: "00000000-0000-4000-8000-000000000003", riotId: "léo la menace#0007", label: "Compte de Léo", region: "EU", lastPlayed: "2026-04-20T20:28:00", lastSeen: "2026-04-20T20:28:00", sensitivity: 0.2, profileCount: 0, backupCount: 2, lastProfile: null, connected: false },
      { puuid: "00000000-0000-4000-8000-000000000004", riotId: "Bureau#NA1", label: "", region: "NA", lastPlayed: null, lastSeen: "2026-04-12T16:52:03", sensitivity: null, profileCount: 0, backupCount: 0, lastProfile: null, connected: false },
    ],
    profiles: [
      { id: "a", name: "Setup principal", source: "Demo#0001", sourcePuuid: P1, created: "2026-11-24T00:10:00", isBackup: false, sensitivity: 0.2, counts, partial: false, full: null },
      { id: "b", name: "Viseur de Léo", source: "léo la menace#0007", sourcePuuid: "x", created: "2026-11-20T18:02:00", isBackup: false, sensitivity: 0.35, counts: { ...counts, Graphismes: 0, Audio: 0 }, partial: true, full: ["Viseur", "Touches"] },
      { id: "d", name: "Config tournoi — 1600 DPI, pas de musique", source: "Duo#EUW", sourcePuuid: "y", created: "2026-11-02T14:40:00", isBackup: false, sensitivity: 0.31, counts: { ...counts, Minimap: 0 }, partial: false, full: null },
    ],
    backups: [
      { id: "c", name: "Sauvegarde Demo#0001 2026-11-24 00-03-40", source: "Demo#0001", sourcePuuid: P1, created: "2026-11-24T00:03:40", isBackup: true, expiresAt: new Date(Date.now() + 42 * 60000).toISOString(), sensitivity: 0.2, counts, partial: false, full: null },
    ],
  };
}

function mockApi(cmd, args = {}) {
  if (!mock) mockInit();
  const wait = (v, ms = 60) => new Promise((r) => setTimeout(() => r(v), ms));
  const err = (msg) => new Promise((_, j) => setTimeout(() => j(new Error(msg)), 60));
  const find = (id) => mock.profiles.find((p) => p.id === id) || mock.backups.find((p) => p.id === id);
  switch (cmd) {
    case "status": return wait({ ...mock.status });
    case "accounts": return wait(mock.accounts.map((a) => ({ ...a, connected: mock.status.connected && a.puuid === mock.status.puuid })));
    case "profiles": return wait({ profiles: mock.profiles, backups: mock.backups });
    case "setLabel": { const a = mock.accounts.find((x) => x.puuid === args.puuid); if (a) a.label = args.label; return wait(true); }
    case "currentSettings":
      if (!mock.status.connected) return err("Aucun compte connecté dans le Riot Client.");
      return wait({ riotId: mock.status.riotId, puuid: mock.status.puuid, categories: mockSettings() }, 200);
    case "saveProfile": {
      const cats = mockSettings();
      const inc = new Set(args.include || []);
      const counts = {}; let total = 0;
      cats.forEach((c) => { total += c.items.length; counts[c.key] = c.items.filter((it) => inc.has(it.id)).length; });
      const p = { id: "p" + Date.now(), name: args.name, source: mock.status.riotId, sourcePuuid: mock.status.puuid, created: new Date().toISOString(), isBackup: false, sensitivity: 0.2, counts, partial: inc.size < total, full: null };
      mock.profiles.unshift(p);
      return wait(p, 250);
    }
    case "renameProfile": { const p = find(args.id); if (p) p.name = args.name; return wait(true); }
    case "deleteProfile":
      mock.profiles = mock.profiles.filter((p) => p.id !== args.id);
      mock.backups = mock.backups.filter((p) => p.id !== args.id);
      return wait(true);
    case "apply": {
      const p = find(args.id);
      const lines = [`  Sauvegarde du compte cible : Sauvegarde ${mock.status.riotId}`,
        ...(args.cats || []).map((k) => `  ${(CATS.find((c) => c.key === k) || { label: k }).label} : ${(p && p.counts[k]) || 0} réglages écrits`),
        "OK  Profil appliqué. Relance Valorant pour voir les changements."];
      return new Promise((r) => {
        lines.forEach((t, i) => setTimeout(() => onEvent({ event: "log", text: t }), 250 * (i + 1)));
        setTimeout(() => r(true), 250 * (lines.length + 1));
      });
    }
    case "exportProfile": return wait("C:\\Users\\toi\\Documents\\" + ((find(args.id) || {}).name || "profil") + ".valosettings");
    case "copyCode": return wait(1834);
    case "importFile": return wait({ imported: ["Profil de Max"], errors: [] });
    case "importCode": {
      if (!String(args.code || "").startsWith("VALOSETTINGS1:")) return err("Ce code n'est pas un code ValoSettings valide.");
      const p = { id: "i" + Date.now(), name: "Profil importé", source: "Inconnu#0000", sourcePuuid: "z", created: new Date().toISOString(), isBackup: false, sensitivity: null, counts: { Viseur: 12 }, partial: true, full: null };
      mock.profiles.unshift(p);
      return wait(p);
    }
    case "checkUpdate": return wait(location.hash.includes("update") ? { current: "1.0.0", latest: "1.1.0", available: true } : { current: "1.1.0", available: false });
    case "openUpdate": return wait(true);
    case "openFolder": return wait(true);
    default: return err("Commande inconnue : " + cmd);
  }
}
