/* ================================================================
   CONFIG  — localStorage ile saklanan uygulama ayarları
   ================================================================ */
const Config = {
    KEY: 'dynamicsai_v1',

    defaults() {
        return {
            gatewayUrl: 'http://localhost:5050',
            apiKey: '',
            model: 'claude-sonnet-4-6',
            userId: '',
            tc: { deploy: 'Online', dynUrl: '', tenant: '', clientId: '', secret: '', user: '', pass: '' }
        };
    },

    load() {
        try {
            const s = localStorage.getItem(this.KEY);
            return s ? Object.assign(this.defaults(), JSON.parse(s)) : this.defaults();
        } catch { return this.defaults(); }
    },

    save(c) { localStorage.setItem(this.KEY, JSON.stringify(c)); },

    isReady(c) {
        return !!(c.gatewayUrl && c.apiKey && c.tc?.dynUrl);
    }
};

/* ================================================================
   MARKDOWN  — hafif inline parser
   ================================================================ */
const MD = {
    render(raw) {
        if (!raw) return '';
        let s = this.esc(raw);

        // fenced code blocks
        s = s.replace(/```(\w*)\n?([\s\S]*?)```/g, (_, l, code) =>
            `<pre><code>${code.trim()}</code></pre>`);

        // inline code
        s = s.replace(/`([^`\n]+)`/g, '<code>$1</code>');

        // headings
        s = s.replace(/^### (.+)$/gm, '<h3>$1</h3>');
        s = s.replace(/^## (.+)$/gm,  '<h2>$1</h2>');
        s = s.replace(/^# (.+)$/gm,   '<h1>$1</h1>');

        // bold / italic
        s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        s = s.replace(/__(.+?)__/g,     '<strong>$1</strong>');
        s = s.replace(/\*([^*\n]+)\*/g, '<em>$1</em>');

        // blockquote
        s = s.replace(/^&gt; (.+)$/gm, '<blockquote>$1</blockquote>');

        // hr
        s = s.replace(/^---$/gm, '<hr>');

        // unordered list items
        s = s.replace(/^[-*] (.+)$/gm, '<li>$1</li>');
        s = s.replace(/(<li>[\s\S]+?<\/li>)(\n(?=<li>)|$)/g, '$1');
        s = s.replace(/(<li>[\s\S]*?<\/li>)+/g, m => `<ul>${m}</ul>`);

        // ordered list items
        s = s.replace(/^\d+\. (.+)$/gm, '<li>$1</li>');

        // links
        s = s.replace(/\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g,
            '<a href="$2" target="_blank" rel="noopener">$1</a>');
        s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g,
            '<a href="$2" target="_blank">$1</a>');

        // paragraphs
        const blocks = s.split(/\n{2,}/);
        s = blocks.map(b => {
            const t = b.trim();
            if (!t) return '';
            if (/^<(h[123]|ul|ol|pre|blockquote|hr)/.test(t)) return t;
            return `<p>${t.replace(/\n/g, '<br>')}</p>`;
        }).join('\n');

        return s;
    },

    esc(t) {
        return String(t)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }
};

/* ================================================================
   API  — GatewayApi ile iletişim
   ================================================================ */
const API = {
    headers(extra) {
        return Object.assign({
            'ngrok-skip-browser-warning': 'true',
            'Content-Type': 'application/json'
        }, extra);
    },

    base(cfg) {
        return cfg.gatewayUrl.replace(/\/+$/, '');
    },

    async chat(cfg, sessionId, message, file) {
        const userId = cfg.userId || xrmUser() || 'user';
        const body = {
            user_id: userId,
            message,
            anthropic_api_key: cfg.apiKey,
            model: cfg.model,
            tenant_context: buildTc(cfg.tc)
        };
        if (sessionId) body.session_id = sessionId;
        if (file)      body.file = file;

        const res = await fetch(`${this.base(cfg)}/api/chat`, {
            method: 'POST',
            headers: this.headers(),
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.error || `HTTP ${res.status}`);
        }
        return res.json();
    },

    async sessions(cfg) {
        const userId = cfg.userId || xrmUser() || 'user';
        const res = await fetch(
            `${this.base(cfg)}/api/chat/sessions?userId=${encodeURIComponent(userId)}`,
            { headers: this.headers() }
        );
        return res.ok ? res.json() : [];
    },

    async messages(cfg, sid) {
        const res = await fetch(
            `${this.base(cfg)}/api/chat/sessions/${sid}`,
            { headers: this.headers() }
        );
        return res.ok ? res.json() : [];
    },

    async deleteSession(cfg, sid) {
        await fetch(`${this.base(cfg)}/api/chat/${sid}`, {
            method: 'DELETE',
            headers: this.headers()
        });
    }
};

function buildTc(tc) {
    if (!tc || !tc.dynUrl) return null;   // zorunlu alan yoksa server DefaultTenant'ı kullansın
    const o = {
        dynamics_url: tc.dynUrl,
        client_id:    tc.clientId || '51f81489-12ee-4a9e-aaae-a2591f45987d'  // well-known Dynamics client
    };
    if (tc.tenant)   o.tenant_id       = tc.tenant;
    if (tc.secret)   o.client_secret   = tc.secret;
    if (tc.user)     o.username        = tc.user;
    if (tc.pass)     o.password        = tc.pass;
    if (tc.deploy)   o.deployment_type = tc.deploy;
    return o;
}

function getXrm() {
    if (typeof Xrm !== 'undefined') return Xrm;
    try { if (window.parent?.Xrm) return window.parent.Xrm; } catch {}
    try { if (window.top?.Xrm)    return window.top.Xrm;    } catch {}
    return null;
}

function xrmUser() {
    try {
        const id = getXrm()?.Utility.getGlobalContext().getUserId();
        return id ? id.replace(/[{}]/g, '') : null;
    }
    catch { return null; }
}

function xrmOrgUrl() {
    try { return getXrm()?.Utility.getGlobalContext().getClientUrl() ?? null; }
    catch { return null; }
}

/* ai_model option set değeri → model string */
function modelOptionToValue(optionValue) {
    return { 100000000: 'claude-sonnet-4-6', 100000001: 'claude-opus-4-7', 100000002: 'claude-haiku-4-5-20251001' }[optionValue]
        || 'claude-sonnet-4-6';
}

/* model string → ai_model option set değeri */
function modelValueToOption(model) {
    return { 'claude-sonnet-4-6': 100000000, 'claude-opus-4-7': 100000001, 'claude-haiku-4-5-20251001': 100000002 }[model]
        ?? 100000000;
}

/* ai_deploymenttype option set değeri → deploy string */
function deployOptionToValue(optionValue) {
    return optionValue === 100000001 ? 'OnPrem' : 'Online';
}

/* deploy string → ai_deploymenttype option set değeri */
function deployValueToOption(deploy) {
    return deploy === 'OnPrem' ? 100000001 : 100000000;
}

/* ================================================================
   APP  — uygulama mantığı
   ================================================================ */
const App = {
    cfg: null,
    sid: null,
    sessions: [],
    busy: false,
    file: null,

    async init() {
        this.cfg = Config.load();
        await this.loadConfig();   // Dynamics entity → localStorage sırasıyla dener
        this.bindAll();
        this.checkReady();
        this.updateBadge();
        await this.refreshSessions();
        // Dynamics WebResource: parent-frame Xrm henüz hazır olmayabilir.
        // userId alınabildiğinde oturumları yeniden yükle.
        this._pollForXrmUserId();
    },

    _pollForXrmUserId() {
        if (this.cfg.userId) return;   // Zaten userId var, gerek yok
        let attempts = 0;
        const poll = async () => {
            const u = xrmUser();
            if (u) {
                this.cfg.userId = u;
                await this.refreshSessions();
            } else if (++attempts < 8) {
                setTimeout(poll, 500);
            }
        };
        setTimeout(poll, 500);
    },

    /* Yapılandırmayı yükleme önceliği:
       1. Dynamics'teyse: crmakad_ai_configuration (isdefault=true) kaydı
       2. Xrm context'ten userId + orgUrl doldur
       3. localStorage'daki manuel ayarlar (fallback)                    */
    async loadConfig() {
        await this.loadFromDynamicsEntity();

        const u = xrmUser();
        if (u && !this.cfg.userId) this.cfg.userId = u;

        const org = xrmOrgUrl();
        if (org && !this.cfg.tc.dynUrl) this.cfg.tc.dynUrl = org;
    },

    async loadFromDynamicsEntity() {
        const xrm = getXrm();
        if (!xrm) return;
        try {
            const result = await xrm.WebApi.retrieveMultipleRecords(
                'ai_configuration',
                '?$filter=ai_isdefault eq true and statecode eq 0&$top=1'
            );
            if (!result.entities.length) return;
            const e = result.entities[0];

            if (e.ai_gatewayurl)      this.cfg.gatewayUrl    = e.ai_gatewayurl;
            if (e.ai_anthropicapikey) this.cfg.apiKey         = e.ai_anthropicapikey;
            if (e.ai_model != null)   this.cfg.model          = modelOptionToValue(e.ai_model);
            if (e.ai_dynamicsurl)     this.cfg.tc.dynUrl      = e.ai_dynamicsurl;
            if (e.ai_tenantid)        this.cfg.tc.tenant      = e.ai_tenantid;
            if (e.ai_clientid)        this.cfg.tc.clientId    = e.ai_clientid;
            if (e.ai_clientsecret)    this.cfg.tc.secret      = e.ai_clientsecret;
            if (e.ai_username)        this.cfg.tc.user        = e.ai_username;
            if (e.ai_password)        this.cfg.tc.pass        = e.ai_password;
            if (e.ai_deploymenttype != null)
                this.cfg.tc.deploy = deployOptionToValue(e.ai_deploymenttype);
        } catch (err) {
            console.warn('ai_configuration yüklenemedi:', err);
        }
    },

    bindAll() {
        $('btnNew').addEventListener('click', () => this.newChat());
        $('btnSettings').addEventListener('click', () => this.openSettings());
        $('btnCloseModal').addEventListener('click', () => this.closeSettings());
        $('btnCancelModal').addEventListener('click', () => this.closeSettings());
        $('btnSaveModal').addEventListener('click', () => this.saveSettings());
        $('btnSend').addEventListener('click', () => this.send());
        $('btnAttach').addEventListener('click', () => $('fileInput').click());
        $('btnRemove').addEventListener('click', () => this.clearFile());
        $('fileInput').addEventListener('change', e => this.onFileInput(e));

        const inp = $('msgInput');
        inp.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); this.send(); }
        });
        inp.addEventListener('input', () => this.autoGrow(inp));

        document.addEventListener('dragenter', e => {
            if (e.dataTransfer?.types?.includes('Files'))
                $('dropZone').classList.add('on');
        });
        document.addEventListener('dragleave', e => {
            if (!e.relatedTarget) $('dropZone').classList.remove('on');
        });
        document.addEventListener('dragover', e => e.preventDefault());
        document.addEventListener('drop', e => {
            e.preventDefault();
            $('dropZone').classList.remove('on');
            const f = e.dataTransfer?.files?.[0];
            if (f) this.loadFile(f);
        });

        $('settingsOverlay').addEventListener('click', e => {
            if (e.target === $('settingsOverlay')) this.closeSettings();
        });
    },

    autoGrow(el) {
        el.style.height = 'auto';
        el.style.height = Math.min(el.scrollHeight, 130) + 'px';
    },

    checkReady() {
        $('configBanner').classList.toggle('visible', !Config.isReady(this.cfg));
    },

    updateBadge() {
        $('modelBadge').textContent = this.cfg.model || 'claude-sonnet-4-6';
    },

    /* ---------- sessions ---------- */
    async refreshSessions() {
        try { this.sessions = await API.sessions(this.cfg); }
        catch { this.sessions = []; }
        this.renderSessions();
    },

    renderSessions() {
        const el = $('sessionsList');
        el.innerHTML = '';
        if (!this.sessions.length) {
            el.innerHTML = '<div style="padding:12px;font-size:12px;color:var(--text-dim);text-align:center">Henüz sohbet yok</div>';
            return;
        }
        this.sessions.forEach(s => {
            const sid = s.sessionId || s.id;
            const row = document.createElement('div');
            row.className = 'session-item' + (sid === this.sid ? ' active' : '');
            row.innerHTML =
                `<span class="session-title" title="${esc(s.title || 'Yeni Sohbet')}">${esc(s.title || 'Yeni Sohbet')}</span>` +
                `<button class="session-del" title="Sil">×</button>`;
            row.querySelector('.session-title').addEventListener('click', () => this.loadSession(sid));
            row.querySelector('.session-del').addEventListener('click', async ev => {
                ev.stopPropagation();
                await this.deleteSession(sid);
            });
            el.appendChild(row);
        });
    },

    async loadSession(sid) {
        this.sid = sid;
        this.renderSessions();
        const s = this.sessions.find(x => (x.sessionId || x.id) === sid);
        $('chatTitle').textContent = s?.title || 'Sohbet';
        $('messages').innerHTML = '<div style="padding:20px;color:var(--text-dim);text-align:center">Yükleniyor…</div>';
        try {
            const msgs = await API.messages(this.cfg, sid);
            if (!msgs || !msgs.length) {
                $('messages').innerHTML = '<div style="padding:20px;color:var(--text-dim);text-align:center">Mesaj bulunamadı</div>';
                return;
            }
            this.renderHistory(msgs);
        } catch (err) {
            $('messages').innerHTML = `<div style="padding:20px;color:var(--error)">Mesajlar yüklenemedi: ${err.message}</div>`;
        }
    },

    renderHistory(msgs) {
        const c = $('messages');
        c.innerHTML = '';
        msgs.forEach(m => {
            try {
                const parsed = typeof m.contentJson === 'string'
                    ? JSON.parse(m.contentJson) : m.contentJson;

                // ContentJson tam mesaj nesnesi: {role, content} veya direkt içerik
                const raw = parsed?.content ?? parsed;

                let text = '';
                if (typeof raw === 'string') {
                    text = raw;
                } else if (Array.isArray(raw)) {
                    text = raw
                        .filter(b => b.type === 'text')
                        .map(b => b.text)
                        .join('\n');
                }

                if (text) this.addBubble(m.role, text, m.role === 'assistant');
            } catch {}
        });
        this.scroll();
    },

    async deleteSession(sid) {
        await API.deleteSession(this.cfg, sid);
        if (this.sid === sid) this.newChat();
        this.sessions = this.sessions.filter(s => (s.sessionId || s.id) !== sid);
        this.renderSessions();
        toast('Sohbet silindi');
    },

    newChat() {
        this.sid = null;
        $('messages').innerHTML =
            '<div class="welcome" id="welcomeMsg">' +
            '<h2>DynamicsAI’ye Hoşgeldiniz</h2>' +
            '<p>Dynamics 365 verilerinizi sorgulamak, analiz etmek ve Excel’e aktarmak için benimle konuşun.</p>' +
            '</div>';
        $('chatTitle').textContent = 'DynamicsAI';
        this.renderSessions();
        $('msgInput').focus();
    },

    /* ---------- send ---------- */
    async send() {
        if (this.busy) return;
        const inp  = $('msgInput');
        const text = inp.value.trim();
        if (!text && !this.file) return;

        if (!Config.isReady(this.cfg)) {
            this.openSettings();
            toast('Lütfen önce ayarları yapılandırın', 'err');
            return;
        }

        this.busy = true;
        inp.value = '';
        this.autoGrow(inp);
        $('btnSend').disabled = true;
        document.getElementById('welcomeMsg')?.remove();

        const preview = this.file ? `\n📎 ${this.file.name}` : '';
        this.addBubble('user', (text || '(dosya)') + preview);

        const spinner = this.showSpinner();

        try {
            const result = await API.chat(this.cfg, this.sid, text || '(dosya)', this.file);
            spinner.remove();
            this.sid = result.session_id;
            this.addBubble('assistant', result.message, true, result.tool_calls_made);
            this.clearFile();
            await this.refreshSessions();
        } catch (err) {
            spinner.remove();
            this.addBubble('assistant', `Hata: ${err.message}`);
            toast(err.message, 'err');
        } finally {
            this.busy = false;
            $('btnSend').disabled = false;
            inp.focus();
        }
    },

    addBubble(role, text, markdown = false, tools = 0) {
        const c  = $('messages');
        const el = document.createElement('div');
        el.className = `msg ${role}`;
        const html = markdown ? MD.render(text) : `<p>${MD.esc(text).replace(/\n/g, '<br>')}</p>`;
        const toolBadge = tools > 0 ? `<span class="tool-badge">${tools} araç çağrısı</span>` : '';
        const time = new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
        el.innerHTML =
            `<div class="bubble">${html}</div>` +
            `<div class="msg-meta">${toolBadge}<span>${time}</span></div>`;
        c.appendChild(el);
        this.scroll();
    },

    showSpinner() {
        const el = document.createElement('div');
        el.className = 'loading-dots';
        el.innerHTML = '<span></span><span></span><span></span>';
        $('messages').appendChild(el);
        this.scroll();
        return el;
    },

    scroll() {
        const c = $('messages');
        c.scrollTop = c.scrollHeight;
    },

    /* ---------- file ---------- */
    async onFileInput(e) {
        const f = e.target.files?.[0];
        if (f) await this.loadFile(f);
        e.target.value = '';
    },

    async loadFile(f) {
        if (f.size > 20 * 1024 * 1024) { toast('Dosya 20 MB’ı aşıyor', 'err'); return; }
        try {
            const data = await readBase64(f);
            this.file = { name: f.name, data, mime_type: f.type || 'application/octet-stream' };
            $('fileName').textContent = f.name;
            $('filePreview').classList.add('on');
        } catch { toast('Dosya okunamadı', 'err'); }
    },

    clearFile() {
        this.file = null;
        $('filePreview').classList.remove('on');
        $('fileName').textContent = '';
    },

    /* ---------- settings ---------- */
    openSettings() {
        const c = this.cfg;
        $('cfgGateway').value  = c.gatewayUrl     || '';
        $('cfgApiKey').value   = c.apiKey          || '';
        $('cfgModel').value    = c.model           || 'claude-sonnet-4-6';
        $('cfgUserId').value   = c.userId          || '';
        $('cfgDeploy').value   = c.tc?.deploy      || 'Online';
        $('cfgDynUrl').value   = c.tc?.dynUrl      || '';
        $('cfgTenant').value   = c.tc?.tenant      || '';
        $('cfgClientId').value = c.tc?.clientId    || '';
        $('cfgSecret').value   = c.tc?.secret      || '';
        $('cfgUser').value     = c.tc?.user        || '';
        $('cfgPass').value     = c.tc?.pass        || '';
        $('settingsOverlay').classList.add('open');
    },

    closeSettings() {
        $('settingsOverlay').classList.remove('open');
    },

    saveSettings() {
        this.cfg = {
            gatewayUrl: $('cfgGateway').value.trim(),
            apiKey:     $('cfgApiKey').value.trim(),
            model:      $('cfgModel').value,
            userId:     $('cfgUserId').value.trim(),
            tc: {
                deploy:   $('cfgDeploy').value,
                dynUrl:   $('cfgDynUrl').value.trim(),
                tenant:   $('cfgTenant').value.trim(),
                clientId: $('cfgClientId').value.trim(),
                secret:   $('cfgSecret').value.trim(),
                user:     $('cfgUser').value.trim(),
                pass:     $('cfgPass').value.trim()
            }
        };
        Config.save(this.cfg);
        this.closeSettings();
        this.checkReady();
        this.updateBadge();
        toast('Ayarlar kaydediliyor…');
        this.saveToDynamics().then(result => {
            toast(result ? 'Ayarlar Dynamics\'e kaydedildi' : 'Ayarlar tarayıcıya kaydedildi', 'ok');
        }).catch(err => {
            console.error('saveToDynamics hata:', err);
            toast('Dynamics hatası: ' + (err?.message || JSON.stringify(err)), 'err');
        });
    },

    async saveToDynamics() {
        const xrm = getXrm();
        if (!xrm) {
            toast('Xrm bulunamadı — Dynamics dışında çalışıyor olabilirsiniz', 'err');
            return false;
        }

        const c = this.cfg;
        const record = {
            ai_name:            'Varsayılan',
            ai_gatewayurl:      c.gatewayUrl       || '',
            ai_anthropicapikey: c.apiKey            || '',
            ai_model:           modelValueToOption(c.model),
            ai_deploymenttype:  deployValueToOption(c.tc?.deploy),
            ai_isdefault:       true,
            ai_dynamicsurl:     c.tc?.dynUrl        || '',
            ai_tenantid:        c.tc?.tenant        || '',
            ai_clientid:        c.tc?.clientId      || '',
            ai_clientsecret:    c.tc?.secret        || '',
            ai_username:        c.tc?.user          || '',
            ai_password:        c.tc?.pass          || ''
        };

        toast('Dynamics\'e bağlanılıyor…');

        const existing = await xrm.WebApi.retrieveMultipleRecords(
            'ai_configuration',
            '?$select=ai_configurationid&$filter=ai_isdefault eq true and statecode eq 0&$top=1'
        );

        if (existing.entities.length) {
            const id = existing.entities[0].ai_configurationid;
            toast('Mevcut kayıt güncelleniyor…');
            await xrm.WebApi.updateRecord('ai_configuration', id, record);
        } else {
            toast('Yeni kayıt oluşturuluyor…');
            await xrm.WebApi.createRecord('ai_configuration', record);
        }
        return true;
    }
};

/* ================================================================
   UTILITIES
   ================================================================ */
function $(id) { return document.getElementById(id); }

function esc(t) {
    return String(t)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function readBase64(file) {
    return new Promise((res, rej) => {
        const r = new FileReader();
        r.onload  = () => res(r.result.split(',')[1]);
        r.onerror = rej;
        r.readAsDataURL(file);
    });
}

let toastTimer;
function toast(msg, type = '') {
    const el = $('toast');
    el.textContent = msg;
    el.className = 'on ' + type;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.className = '', 3000);
}

/* ================================================================
   BOOTSTRAP
   ================================================================ */
App.init();
