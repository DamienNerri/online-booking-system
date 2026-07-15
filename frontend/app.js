// Front minimal (vanilla JS) — même origine que l'API (proxy Nginx), donc pas de CORS.
"use strict";

const state = {
    token: localStorage.getItem("token") || null,
    email: localStorage.getItem("email") || null,
    role: localStorage.getItem("role") || null,
    selected: new Set(),
    bookings: JSON.parse(localStorage.getItem("bookings") || "[]"),
};

// --- Helpers DOM ---
const $ = (id) => document.getElementById(id);
const show = (el) => el.classList.remove("hidden");
const hide = (el) => el.classList.add("hidden");

function message(text, kind = "error") {
    const box = $("message");
    box.textContent = text;
    box.className = `message ${kind}`;
    show(box);
    if (kind === "success") setTimeout(() => hide(box), 3000);
}
function clearMessage() { hide($("message")); }

// --- Appels API ---
async function api(path, { method = "GET", body = null, auth = false } = {}) {
    const headers = {};
    if (body) headers["Content-Type"] = "application/json";
    if (auth && state.token) headers["Authorization"] = `Bearer ${state.token}`;

    const res = await fetch(path, { method, headers, body: body ? JSON.stringify(body) : null });

    // Affiche le nœud ayant traité la requête (démonstration de la distribution).
    if (path === "/health") { try { return await res.json(); } catch { return null; } }

    let data = null;
    try { data = await res.json(); } catch { /* réponse sans corps */ }

    if (!res.ok) {
        const msg = data && data.error ? data.error.message : `Erreur HTTP ${res.status}`;
        throw new Error(msg);
    }
    return data;
}

async function refreshNode() {
    try {
        const h = await api("/health");
        if (h && h.node) $("node").textContent = `nœud ${h.node.substring(0, 8)}`;
    } catch { /* ignore */ }
}

// --- Session ---
function persistSession() {
    if (state.token) {
        localStorage.setItem("token", state.token);
        localStorage.setItem("email", state.email);
        localStorage.setItem("role", state.role);
    } else {
        localStorage.removeItem("token");
        localStorage.removeItem("email");
        localStorage.removeItem("role");
    }
}
function persistBookings() {
    localStorage.setItem("bookings", JSON.stringify(state.bookings));
}

function renderSession() {
    if (state.token) {
        hide($("auth"));
        show($("app"));
        show($("session"));
        $("who").textContent = state.email;
        renderBookings();
        refreshNode();
    } else {
        show($("auth"));
        hide($("app"));
        hide($("session"));
    }
}

async function authenticate(path) {
    clearMessage();
    const email = $("email").value.trim();
    const password = $("password").value;
    try {
        const res = await api(path, { method: "POST", body: { email, password } });
        state.token = res.token;
        state.email = res.email;
        state.role = res.role;
        persistSession();
        renderSession();
        message("Connecté.", "success");
    } catch (e) {
        message(e.message);
    }
}

function logout() {
    state.token = state.email = state.role = null;
    state.selected.clear();
    persistSession();
    renderSession();
}

// --- Disponibilités ---
async function search() {
    clearMessage();
    state.selected.clear();
    const type = $("type").value;
    const from = $("from").value;
    const to = $("to").value;
    try {
        const items = await api(`/api/availability?type=${encodeURIComponent(type)}&from=${from}&to=${to}`);
        renderResults(items);
        refreshNode();
    } catch (e) {
        message(e.message);
    }
}

function renderResults(items) {
    const box = $("results");
    box.innerHTML = "";
    if (!items || items.length === 0) {
        box.innerHTML = '<p class="muted">Aucune disponibilité sur cette période.</p>';
        hide($("btn-book"));
        return;
    }
    items.slice(0, 60).forEach((it) => {
        const el = document.createElement("div");
        el.className = "slot";
        el.innerHTML = `<div>Ressource #${it.resourceId}</div>
            <div class="sid">slot ${it.slotId}</div>
            <div>${it.periodStart} → ${it.periodEnd}</div>`;
        el.onclick = () => {
            if (state.selected.has(it.slotId)) { state.selected.delete(it.slotId); el.classList.remove("selected"); }
            else { state.selected.add(it.slotId); el.classList.add("selected"); }
            $("btn-book").classList.toggle("hidden", state.selected.size === 0);
        };
        box.appendChild(el);
    });
}

// --- Réservations ---
async function book() {
    clearMessage();
    const slotIds = Array.from(state.selected);
    if (slotIds.length === 0) return;
    try {
        const res = await api("/api/bookings", { method: "POST", body: { slotIds }, auth: true });
        state.bookings.unshift({
            bookingId: res.bookingId, status: res.status,
            expiresAt: res.expiresAt, slotIds: res.slotIds,
        });
        persistBookings();
        renderBookings();
        message(`Réservation ${res.bookingId} créée (hold).`, "success");
        search(); // rafraîchit la disponibilité
    } catch (e) {
        message(e.message);
    }
}

async function confirmBooking(id) {
    clearMessage();
    try {
        const res = await api(`/api/bookings/${id}/confirm`, { method: "POST", auth: true });
        updateBooking(id, { status: res.status, expiresAt: null });
        message(`Réservation ${id} confirmée.`, "success");
    } catch (e) {
        // Le hold a pu expirer entre-temps : refléter le vrai statut côté UI.
        if (/expir/i.test(e.message)) { updateBooking(id, { status: "EXPIRED", expiresAt: null }); search(); }
        message(e.message);
    }
}

async function cancelBooking(id) {
    clearMessage();
    try {
        await api(`/api/bookings/${id}`, { method: "DELETE", auth: true });
        updateBooking(id, { status: "CANCELLED", expiresAt: null });
        message(`Réservation ${id} annulée.`, "success");
        search();
    } catch (e) { message(e.message); }
}

function updateBooking(id, patch) {
    const b = state.bookings.find((x) => x.bookingId === id);
    if (b) Object.assign(b, patch);
    persistBookings();
    renderBookings();
}

function renderBookings() {
    const box = $("bookings");
    if (state.bookings.length === 0) {
        box.innerHTML = '<p class="muted">Aucune réservation pour l\'instant.</p>';
        return;
    }
    box.innerHTML = "";
    state.bookings.forEach((b) => {
        const el = document.createElement("div");
        el.className = "booking";
        const holdInfo = b.status === "HOLD" && b.expiresAt
            ? ` · <span class="countdown" data-exp="${b.expiresAt}" data-id="${b.bookingId}"></span>` : "";
        el.innerHTML = `<div class="meta">
                <strong>#${b.bookingId}</strong>
                <span class="badge ${b.status}">${b.status}</span>
                <span class="muted">slots ${b.slotIds.join(", ")}${holdInfo}</span>
            </div>`;
        const actions = document.createElement("div");
        actions.className = "row";
        if (b.status === "HOLD") {
            const c = document.createElement("button");
            c.textContent = "Confirmer"; c.className = "ok";
            c.onclick = () => confirmBooking(b.bookingId);
            actions.appendChild(c);
        }
        if (b.status === "HOLD" || b.status === "CONFIRMED") {
            const x = document.createElement("button");
            x.textContent = "Annuler"; x.className = "danger";
            x.onclick = () => cancelBooking(b.bookingId);
            actions.appendChild(x);
        }
        el.appendChild(actions);
        box.appendChild(el);
    });
}

// Met à jour les comptes à rebours des holds ; bascule en EXPIRED à échéance.
function tickCountdowns() {
    let changed = false;
    document.querySelectorAll(".countdown").forEach((el) => {
        const exp = new Date(el.dataset.exp).getTime();
        const rem = Math.floor((exp - Date.now()) / 1000);
        const id = Number(el.dataset.id);
        if (rem <= 0) {
            const b = state.bookings.find((x) => x.bookingId === id);
            if (b && b.status === "HOLD") { b.status = "EXPIRED"; changed = true; }
        } else {
            const m = String(Math.floor(rem / 60)).padStart(2, "0");
            const s = String(rem % 60).padStart(2, "0");
            el.textContent = `expire dans ${m}:${s}`;
        }
    });
    if (changed) { persistBookings(); renderBookings(); }
}
setInterval(tickCountdowns, 1000);

// --- Câblage ---
$("btn-login").onclick = () => authenticate("/api/auth/login");
$("btn-register").onclick = () => authenticate("/api/auth/register");
$("logout").onclick = logout;
$("btn-search").onclick = search;
$("btn-book").onclick = book;

renderSession();
