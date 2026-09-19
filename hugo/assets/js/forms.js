/*
 * forms.js - renderer for the in-house form system.
 *
 * Embedding: <div data-form="slug" data-api="/api"></div> (see layouts/shortcodes/form.html).
 * Fetches the published definition and an anti-spam token from the backend, renders semantic HTML
 * with stable eq-* classes (no inline styling, no iframe - the theme of the page applies), validates
 * by the same rules as the server (for UX only; the server checks again) and submits.
 * Quizzes run question by question including branches; points and result are known to the server only.
 *
 * No dependencies, ES2018, ~12 KB. Markup contract: see SPEC.md -> "Markup & Klassen".
 */
(function () {
  'use strict';

  var HONEYPOT = 'website';
  // whitelist of "safe" formats - has to match the server list (UploadRules).
  var FILE_ACCEPT = '.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.png,.jpg,.jpeg,.gif,.webp';
  var FILE_MAX = 10 * 1024 * 1024;

  function init() {
    var hosts = document.querySelectorAll('[data-form]:not([data-eq-ready])');
    Array.prototype.forEach.call(hosts, function (host) {
      host.setAttribute('data-eq-ready', '1');
      new FormWidget(host).load();
    });
  }

  function FormWidget(host) {
    this.host = host;
    this.slug = host.getAttribute('data-form');
    this.api = (host.getAttribute('data-api') || '/api').replace(/\/$/, '');
    this.lang = host.getAttribute('data-lang') || (document.documentElement.lang || 'de').split('-')[0];
    this.def = null;
    this.token = null;
    // #21: admin test mode - loads the draft, sends no anti-spam token, counts no view, and shows a
    // protocol instead of submitting for real. Only reachable from the admin's authenticated test page.
    this.test = host.getAttribute('data-test') === '1';
    this.quiz = null; // { index, answers: {}, history: [] }
  }

  // UI and error texts come from the server (PublicFormView.strings, language of the definition); German is the fallback.
  FormWidget.prototype.t = function (key, fallback) {
    return (this.def && this.def.strings && this.def.strings[key]) || fallback;
  };

  FormWidget.prototype.load = function () {
    var self = this;
    self.host.innerHTML = '<div class="eq-loading" aria-busy="true"></div>';
    if (self.test) {
      // #21: render the DRAFT as a visitor would see it; a test needs no anti-spam token.
      fetchJson(self.api + '/manage/forms/' + encodeURIComponent(self.slug) + '/test-form?lang=' + encodeURIComponent(self.lang), { cache: 'no-store' })
        .then(function (def) { self.def = def; self.render(); })
        .catch(function (err) { self.host.innerHTML = '<div class="eq-message eq-message--error">' + esc(messageFor(err, 'Das Formular konnte nicht geladen werden.')) + '</div>'; });
      return;
    }
    Promise.all([
      fetchJson(self.api + '/forms/' + encodeURIComponent(self.slug) + '?lang=' + encodeURIComponent(self.lang)),
      fetchJson(withLang(self.api + '/forms/' + encodeURIComponent(self.slug) + '/token', self.lang), { cache: 'no-store' })
    ]).then(function (r) {
      self.def = r[0];
      self.token = r[1].token;
      self.render();
    }).catch(function (err) {
      self.host.innerHTML = '<div class="eq-message eq-message--error">' + esc(messageFor(err, 'Das Formular konnte nicht geladen werden.')) + '</div>';
    });
  };

  /* ---------- Rendering ---------- */

  FormWidget.prototype.render = function () {
    var d = this.def;
    var form = el('form', { 'class': 'eq-form eq-form--' + d.type + ' eq-form--' + d.slug, 'data-form': d.slug, 'data-version': d.version, novalidate: 'novalidate' });
    if (d.intro) form.appendChild(el('p', { 'class': 'eq-form__intro' }, d.intro));

    if (d.quiz) {
      this.quiz = { index: 0, answers: {}, history: [] };
      this.quizContainer = el('div', { 'class': 'eq-quiz' });
      form.appendChild(this.quizContainer);
      this.contactSection = el('div', { 'class': 'eq-quiz__contact', hidden: 'hidden' });
      d.fields.forEach(function (f) { var n = this.renderField(f); if (n) this.contactSection.appendChild(n); }, this);
      this.contactSection.appendChild(this.actions(d.submitLabel || 'Ergebnis anzeigen'));
      form.appendChild(this.contactSection);
      this.renderQuestion();
    } else {
      var pages = splitPages(d.fields);
      if (pages.length > 1) this.renderPages(form, pages, d);
      else {
        d.fields.forEach(function (f) { var n = this.renderField(f); if (n) form.appendChild(n); }, this);
        form.appendChild(this.actions(d.submitLabel || 'Absenden'));
      }
    }

    form.appendChild(this.honeypot());
    this.message = el('div', { 'class': 'eq-message', hidden: 'hidden', role: 'status', 'aria-live': 'polite' });
    form.appendChild(this.message);

    form.addEventListener('submit', this.onSubmit.bind(this));
    this.form = form;
    this.host.innerHTML = '';
    this.host.appendChild(form);
    this.watchVisibility();

    // Drop-off statistics without personal data: "view" once when rendering, "start" once on the first input.
    // A test never counts (#21, AC7): the view count must not move for the admin's own trial.
    if (!this.test) {
      this.track('view');
      var self = this;
      var started = function () { form.removeEventListener('input', started); form.removeEventListener('change', started); self.track('start'); };
      form.addEventListener('input', started);
      form.addEventListener('change', started);
    }
  };

  FormWidget.prototype.track = function (type) {
    var url = this.api + '/forms/' + encodeURIComponent(this.slug) + '/events';
    var body = JSON.stringify({ type: type });
    try {
      if (navigator.sendBeacon && navigator.sendBeacon(url, new Blob([body], { type: 'application/json' }))) return;
    } catch (e) { /* a blob beacon can fail on CSP - then fetch */ }
    fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body, keepalive: true }).catch(function () { /* statistics, deliberately quiet */ });
  };

  /* ---------- Multi-page forms (page break fields split the field list) ---------- */

  FormWidget.prototype.renderPages = function (form, pages, d) {
    var self = this;
    this.pageIndex = 0;
    this.pageEls = [];
    this.pageFields = pages;
    var progress = el('p', { 'class': 'eq-form__pages', 'aria-live': 'polite' });
    form.appendChild(progress);
    this.pagesProgress = progress;

    pages.forEach(function (page, i) {
      var div = el('div', { 'class': 'eq-page', 'data-page': String(i + 1) });
      if (i > 0) div.hidden = true;
      if (page.marker && page.marker.label) div.appendChild(el('h3', { 'class': 'eq-section' }, page.marker.label));
      page.fields.forEach(function (f) { var n = self.renderField(f); if (n) div.appendChild(n); });

      var nav = el('div', { 'class': 'eq-actions eq-page__nav' });
      if (i > 0) {
        var back = el('button', { type: 'button', 'class': 'eq-quiz__back' }, self.t('back', 'Zurück'));
        back.addEventListener('click', function () { self.showPage(i - 1); });
        nav.appendChild(back);
      }
      if (i < pages.length - 1) {
        var next = el('button', { type: 'button', 'class': 'eq-submit' }, self.t('next', 'Weiter'));
        next.addEventListener('click', function () { self.nextPage(i); });
        nav.appendChild(next);
      } else {
        nav.appendChild(el('button', { type: 'submit', 'class': 'eq-submit' }, d.submitLabel || 'Absenden'));
      }
      div.appendChild(nav);
      form.appendChild(div);
      self.pageEls.push(div);
    });
    this.updatePagesProgress();
  };

  FormWidget.prototype.showPage = function (index) {
    this.pageIndex = index;
    this.pageEls.forEach(function (p, i) { p.hidden = i !== index; });
    this.updatePagesProgress();
    var first = this.pageEls[index].querySelector('input:not([type=hidden]), select, textarea');
    if (first) first.focus();
  };

  FormWidget.prototype.updatePagesProgress = function () {
    this.pagesProgress.textContent = (this.pageIndex + 1) + ' / ' + this.pageEls.length;
  };

  FormWidget.prototype.nextPage = function (index) {
    var values = this.collect();
    var pageIds = {};
    this.pageFields[index].fields.forEach(function (f) { pageIds[f.id] = true; });
    var errors = this.validate(values, pageIds);
    if (Object.keys(errors).length) { this.showErrors(errors); return; }
    this.showErrors({});
    this.showPage(index + 1);
  };

  function splitPages(fields) {
    var pages = [{ marker: null, fields: [] }];
    fields.forEach(function (f) {
      if (f.type === 'page') pages.push({ marker: f, fields: [] });
      else pages[pages.length - 1].fields.push(f);
    });
    return pages.filter(function (p, i) { return p.fields.length || i === 0; });
  }

  /* ---------- Conditional visibility (visibleIf points at an earlier field) ---------- */

  // Like the server: evaluate values in field order, drop invisible values - that way chains work too.
  FormWidget.prototype.hiddenByCondition = function (values) {
    var hidden = {};
    var byId = {};
    this.def.fields.forEach(function (f) { byId[f.id] = f; });
    this.def.fields.forEach(function (f) {
      if (!f.visibleIf) return;
      var other = byId[f.visibleIf.field];
      var visible = true;
      if (other) {
        var v = hidden[other.id] ? '' : (values[other.id] || '').trim();
        if (other.type === 'checkbox' || other.type === 'consent') visible = v === 'true';
        else if (f.visibleIf.options && f.visibleIf.options.length && other.options) {
          var wanted = {};
          f.visibleIf.options.forEach(function (i) { if (other.options[i] !== undefined) wanted[other.options[i]] = true; });
          if (other.type === 'multiselect') {
            var picked = []; try { picked = JSON.parse(v || '[]'); } catch (e) { picked = []; }
            visible = picked.some(function (p) { return wanted[p]; });
          } else visible = !!wanted[v];
        } else visible = v.length > 0;
      }
      if (!visible) hidden[f.id] = true;
    });
    return hidden;
  };

  FormWidget.prototype.watchVisibility = function () {
    var self = this;
    var apply = function () {
      var hidden = self.hiddenByCondition(self.collectRaw());
      Array.prototype.forEach.call(self.form.querySelectorAll('.eq-field[data-field]'), function (wrap) {
        var id = wrap.getAttribute('data-field');
        var f = self.def.fields.filter(function (x) { return x.id === id; })[0];
        if (!f || !f.visibleIf) return;
        wrap.hidden = !!hidden[id];
      });
    };
    this.form.addEventListener('change', apply);
    this.form.addEventListener('input', apply);
    apply();
  };

  FormWidget.prototype.actions = function (label) {
    var wrap = el('div', { 'class': 'eq-actions' });
    wrap.appendChild(el('button', { type: 'submit', 'class': 'eq-submit' }, label));
    return wrap;
  };

  FormWidget.prototype.honeypot = function () {
    // Invisible through the hidden attribute, no CSS needed. Bots fill it in anyway.
    return el('input', { type: 'text', name: HONEYPOT, tabindex: '-1', autocomplete: 'off', hidden: 'hidden', 'aria-hidden': 'true' });
  };

  FormWidget.prototype.renderField = function (f) {
    var id = 'eq-' + this.slug + '-' + f.id;
    if (f.type === 'section') return el('h3', { 'class': 'eq-section' }, f.label);
    if (f.type === 'divider') return el('hr', { 'class': 'eq-divider' });
    if (f.type === 'page') return null;               // page breaks only add structure; no effect in the quiz contact step
    if (f.type === 'hidden') return el('input', { type: 'hidden', name: f.id, value: hiddenValue(f) });

    var wrap = el('div', { 'class': 'eq-field eq-field--' + f.type + (f.required ? ' eq-field--required' : ''), 'data-field': f.id });
    var req = f.required ? [' ', el('span', { 'class': 'eq-field__required', 'aria-hidden': 'true' }, '*')] : [];

    if (f.type === 'consent' || f.type === 'checkbox') {
      var lab = el('label', { 'class': 'eq-field__label', 'for': id });
      lab.appendChild(el('input', { 'class': 'eq-field__control', type: 'checkbox', id: id, name: f.id, required: f.required }));
      lab.appendChild(el('span', {}, [f.type === 'consent' ? (f.text || f.label) : f.label].concat(req)));
      wrap.appendChild(lab);
    } else {
      var isMulti = f.type === 'multiselect' || f.type === 'rating';
      // Multi-select and rating render a group instead of a control: the label gets an id of its own (aria-labelledby),
      // a "for" pointing at an id that is never assigned would be wrong.
      wrap.appendChild(el('label', isMulti ? { 'class': 'eq-field__label', id: id + '-label' } : { 'class': 'eq-field__label', 'for': id }, [f.label].concat(req)));
      var ctl;
      if (f.type === 'textarea') ctl = el('textarea', { 'class': 'eq-field__control', id: id, name: f.id, placeholder: f.placeholder, required: f.required, maxlength: f.maxLength || 4000 });
      else if (f.type === 'select') {
        ctl = el('select', { 'class': 'eq-field__control', id: id, name: f.id, required: f.required });
        ctl.appendChild(el('option', { value: '' }, this.t('choose', 'Bitte wählen')));
        (f.options || []).forEach(function (o) { ctl.appendChild(el('option', { value: o }, o)); });
      } else if (f.type === 'multiselect') {
        ctl = el('div', { 'class': 'eq-field__options', role: 'group', 'aria-labelledby': id + '-label' });
        (f.options || []).forEach(function (o, i) {
          var l = el('label', { 'class': 'eq-field__option' });
          l.appendChild(el('input', { 'class': 'eq-field__control', type: 'checkbox', name: f.id, value: o, id: id + '-' + i }));
          l.appendChild(el('span', {}, ' ' + o));
          ctl.appendChild(l);
        });
      } else if (f.type === 'file') {
        ctl = el('input', { 'class': 'eq-field__control', type: 'file', id: id, name: f.id, required: f.required, accept: FILE_ACCEPT });
        var status = el('p', { 'class': 'eq-field__file-status', hidden: 'hidden' });
        var self = this;
        ctl.addEventListener('change', function () { self.uploadFile(f, ctl, wrap, status); });
        wrap.appendChild(ctl);
        wrap.appendChild(status);
        if (f.help) wrap.appendChild(el('p', { 'class': 'eq-field__help' }, f.help));
        wrap.appendChild(el('p', { 'class': 'eq-field__error', hidden: 'hidden' }));
        return wrap;
      } else if (f.type === 'rating') {
        // 1..n as a radio group - the theme styles the options into buttons or stars.
        var steps = Math.min(Math.max(parseInt(f.max, 10) || 5, 2), 10);
        ctl = el('div', { 'class': 'eq-field__scale', role: 'radiogroup', 'aria-labelledby': id + '-label' });
        for (var s = 1; s <= steps; s++) {
          var so = el('label', { 'class': 'eq-field__scale-option' });
          so.appendChild(el('input', { type: 'radio', name: f.id, value: String(s), id: id + '-' + s }));
          so.appendChild(el('span', {}, String(s)));
          ctl.appendChild(so);
        }
      } else {
        var type = f.type === 'email' ? 'email' : f.type === 'number' ? 'number' : f.type === 'date' ? 'date' : f.type === 'tel' ? 'tel' : 'text';
        ctl = el('input', { 'class': 'eq-field__control', type: type, id: id, name: f.id, placeholder: f.placeholder, required: f.required,
          autocomplete: autocompleteFor(f), maxlength: f.type === 'text' ? (f.maxLength || 200) : null, min: f.min, max: f.max, step: f.type === 'number' ? 'any' : null });
      }
      wrap.appendChild(ctl);
      if (f.help) wrap.appendChild(el('p', { 'class': 'eq-field__help' }, f.help));
    }
    wrap.appendChild(el('p', { 'class': 'eq-field__error', hidden: 'hidden' }));
    return wrap;
  };

  /* ---------- Quiz ---------- */

  FormWidget.prototype.renderQuestion = function () {
    var q = this.def.quiz.questions[this.quiz.index];
    var c = this.quizContainer;
    c.innerHTML = '';
    var seen = this.quiz.history.length + 1;
    var total = this.def.quiz.questions.length;
    var pct = Math.round(seen / (total + 1) * 100);
    var prog = el('div', { 'class': 'eq-quiz__progress', role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(pct) });
    prog.appendChild(el('div', { 'class': 'eq-quiz__progress-bar', style: 'width:' + pct + '%' })); // einzige Inline-Ausnahme
    c.appendChild(prog);

    var fs = el('fieldset', { 'class': 'eq-quiz__question', 'data-question': q.id });
    fs.appendChild(el('legend', { 'class': 'eq-quiz__question-text' }, this.t('question', 'Frage') + ' ' + seen + ': ' + q.text));
    var self = this;
    q.options.forEach(function (o) {
      var l = el('label', { 'class': 'eq-quiz__option' + (self.quiz.answers[q.id] === o.id ? ' eq-quiz__option--selected' : '') });
      var r = el('input', { type: 'radio', name: q.id, value: o.id, required: true });
      if (self.quiz.answers[q.id] === o.id) r.checked = true;
      r.addEventListener('change', function () {
        Array.prototype.forEach.call(fs.querySelectorAll('.eq-quiz__option'), function (x) { x.classList.remove('eq-quiz__option--selected'); });
        l.classList.add('eq-quiz__option--selected');
      });
      l.appendChild(r);
      l.appendChild(el('span', {}, ' ' + o.label));
      fs.appendChild(l);
    });
    fs.appendChild(el('p', { 'class': 'eq-field__error', hidden: 'hidden' }));
    c.appendChild(fs);

    var nav = el('div', { 'class': 'eq-quiz__nav' });
    if (this.quiz.history.length) {
      var back = el('button', { type: 'button', 'class': 'eq-quiz__back' }, this.t('back', 'Zurück'));
      back.addEventListener('click', function () { self.quiz.index = self.quiz.history.pop(); self.renderQuestion(); });
      nav.appendChild(back);
    }
    var next = el('button', { type: 'button', 'class': 'eq-submit', 'data-action': 'next' }, this.t('next', 'Weiter'));
    next.addEventListener('click', function () { self.nextQuestion(q, fs); });
    nav.appendChild(next);
    c.appendChild(nav);
  };

  FormWidget.prototype.nextQuestion = function (q, fs) {
    var checked = fs.querySelector('input[type=radio]:checked');
    var err = fs.querySelector('.eq-field__error');
    if (!checked) { err.textContent = this.t('answerRequired', 'Bitte eine Antwort wählen.'); err.hidden = false; fs.classList.add('eq-field--error'); return; }
    this.quiz.answers[q.id] = checked.value;
    var opt = q.options.filter(function (o) { return o.id === checked.value; })[0];
    this.quiz.history.push(this.quiz.index);

    var qs = this.def.quiz.questions;
    var nextIndex;
    if (opt.next && opt.next.indexOf('result:') === 0) nextIndex = qs.length;
    else if (opt.next) nextIndex = qs.map(function (x) { return x.id; }).indexOf(opt.next);
    else nextIndex = this.quiz.index + 1;
    if (nextIndex < 0) nextIndex = this.quiz.index + 1;

    if (nextIndex >= qs.length) this.showContactStep();
    else { this.quiz.index = nextIndex; this.renderQuestion(); }
  };

  FormWidget.prototype.showContactStep = function () {
    this.quizContainer.innerHTML = '';
    var prog = el('div', { 'class': 'eq-quiz__progress', role: 'progressbar', 'aria-valuenow': '100' });
    prog.appendChild(el('div', { 'class': 'eq-quiz__progress-bar', style: 'width:100%' }));
    this.quizContainer.appendChild(prog);
    if (this.def.quiz.collectEmail === 'none' || !this.def.fields.length) {
      this.skipContact = true;                                  // no contact step: collect nothing, validate nothing
      this.submit();
      return;
    }
    this.contactSection.hidden = false;
    var first = this.contactSection.querySelector('input, select, textarea');
    if (first) first.focus();
  };

  /* ---------- File upload (right when picking; the value is the server handle) ---------- */

  FormWidget.prototype.uploadFile = function (f, ctl, wrap, status) {
    var self = this;
    var err = wrap.querySelector('.eq-field__error');
    err.hidden = true; wrap.classList.remove('eq-field--error');
    delete wrap.dataset.eqUpload;
    var file = ctl.files && ctl.files[0];
    if (!file) { status.hidden = true; return; }
    var fail = function (msg) { ctl.value = ''; status.hidden = true; err.textContent = msg; err.hidden = false; wrap.classList.add('eq-field--error'); };
    if (file.size > FILE_MAX) { fail(self.t('uploadTooBig', 'Die Datei ist zu groß (höchstens {max} MB).').replace('{max}', '10')); return; }
    if (FILE_ACCEPT.indexOf((file.name.match(/\.[^.]+$/) || [''])[0].toLowerCase()) < 0) { fail(self.t('uploadType', 'Dieser Dateityp ist nicht erlaubt.')); return; }

    status.textContent = self.t('uploading', 'lädt hoch …'); status.hidden = false;
    self.uploading = (self.uploading || 0) + 1;
    var data = new FormData();
    data.append('token', self.token);
    data.append('file', file);
    fetchJson(withLang(self.api + '/forms/' + encodeURIComponent(self.slug) + '/uploads', self.lang), { method: 'POST', body: data })
      .then(function (r) {
        wrap.dataset.eqUpload = JSON.stringify(r);
        status.textContent = '✓ ' + r.name + ' (' + Math.round(r.size / 1024) + ' KB)';
      })
      .catch(function (e) { fail(messageFor(e, self.t('uploadInvalid', 'Die Datei konnte nicht übernommen werden – bitte erneut hochladen.'))); })
      .then(function () { self.uploading--; });
  };

  /* ---------- Validation and submitting ---------- */

  FormWidget.prototype.collectRaw = function () {
    var values = {};
    this.def.fields.forEach(function (f) {
      if (f.type === 'section' || f.type === 'divider' || f.type === 'page') return;
      if (this.skipContact && f.type !== 'hidden') return;      // quiz without a contact step: hidden fields only (UTM and friends)
      if (f.type === 'multiselect') {
        var picked = Array.prototype.map.call(this.form.querySelectorAll('[name="' + f.id + '"]:checked'), function (x) { return x.value; });
        if (picked.length) values[f.id] = JSON.stringify(picked);
        return;
      }
      if (f.type === 'rating') {
        var sel = this.form.querySelector('[name="' + f.id + '"]:checked');
        if (sel) values[f.id] = sel.value;
        return;
      }
      if (f.type === 'file') {
        var fw = this.form.querySelector('.eq-field[data-field="' + f.id + '"]');
        if (fw && fw.dataset.eqUpload) values[f.id] = fw.dataset.eqUpload;
        return;
      }
      var ctl = this.form.querySelector('[name="' + f.id + '"]');
      if (!ctl) return;
      if (ctl.type === 'checkbox') { if (ctl.checked) values[f.id] = 'true'; return; }
      if (ctl.value !== '') values[f.id] = ctl.value;
    }, this);
    return values;
  };

  FormWidget.prototype.collect = function () {
    // Conditionally invisible fields are not sent along (the server drops them anyway).
    var values = this.collectRaw();
    var hidden = this.hiddenByCondition(values);
    Object.keys(hidden).forEach(function (id) { delete values[id]; });
    return values;
  };

  FormWidget.prototype.validate = function (values, onlyIds) {
    var errors = {};
    if (this.skipContact) return errors;                        // the fields were never shown - the server checks all the same
    var hidden = this.hiddenByCondition(values);
    this.def.fields.forEach(function (f) {
      if (f.type === 'section' || f.type === 'divider' || f.type === 'page' || f.type === 'hidden') return;
      if (hidden[f.id]) return;
      if (onlyIds && !onlyIds[f.id]) return;                    // a page change only checks the current page
      var v = (values[f.id] || '').trim();
      var required = f.required || (f.type === 'email' && this.def.quiz && this.def.quiz.collectEmail === 'required');
      if (!v) { if (required) errors[f.id] = f.type === 'consent' ? this.t('consentRequired', 'Bitte bestätige die Einwilligung.') : this.t('required', 'Pflichtfeld.'); return; }
      if (f.type === 'email' && !/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v)) errors[f.id] = this.t('email', 'Bitte eine gültige E-Mail-Adresse eingeben.');
      if (f.type === 'number' && isNaN(parseFloat(v.replace(',', '.')))) errors[f.id] = this.t('number', 'Bitte eine Zahl eingeben.');
      if (f.type === 'tel' && !/^[0-9 +\-\/().]{5,30}$/.test(v)) errors[f.id] = this.t('tel', 'Bitte eine gültige Telefonnummer eingeben.');
    }, this);
    var emailF = this.def.fields.filter(function (f) { return f.type === 'email'; })[0];
    var consentF = this.def.fields.filter(function (f) { return f.type === 'consent'; })[0];
    if (emailF && consentF && !consentF.required && values[emailF.id] && !values[consentF.id]
        && (!onlyIds || onlyIds[consentF.id])) errors[consentF.id] = this.t('consentIfEmail', 'Bitte bestätige die Einwilligung, wenn du eine E-Mail-Adresse angibst.');
    return errors;
  };

  FormWidget.prototype.showErrors = function (errors) {
    var first = null;
    Array.prototype.forEach.call(this.form.querySelectorAll('.eq-field'), function (wrap) {
      var id = wrap.getAttribute('data-field');
      var err = wrap.querySelector('.eq-field__error');
      if (errors[id]) { wrap.classList.add('eq-field--error'); err.textContent = errors[id]; err.hidden = false; first = first || wrap; }
      else { wrap.classList.remove('eq-field--error'); err.hidden = true; }
    });
    if (first && this.pageEls) {
      // The error may sit on another page (the server checks everything) - page there.
      var page = first.closest('.eq-page');
      if (page && page.hidden) this.showPage(this.pageEls.indexOf(page));
    }
    if (first) { var ctl = first.querySelector('.eq-field__control'); if (ctl) ctl.focus(); }
  };

  FormWidget.prototype.onSubmit = function (e) {
    e.preventDefault();
    this.submit();
  };

  FormWidget.prototype.submit = function () {
    if (this.uploading) { this.say(this.t('uploading', 'lädt hoch …'), 'error'); return; }
    var values = this.collect();
    var errors = this.validate(values);
    if (Object.keys(errors).length) { this.showErrors(errors); return; }
    this.showErrors({});

    var self = this;
    self.form.classList.add('eq-form--busy');
    Array.prototype.forEach.call(self.form.querySelectorAll('.eq-submit'), function (b) { b.disabled = true; });

    if (self.test) {
      // #21: run against the draft - no token, no honeypot; the response is the protocol, not a submission.
      var testBody = { lang: self.lang, values: values };
      if (self.quiz) testBody.answers = self.quiz.answers;
      fetchJson(withLang(self.api + '/manage/forms/' + encodeURIComponent(self.slug) + '/test', self.lang), {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(testBody)
      }).then(function (report) {
        self.renderTestReport(report);
      }).catch(function (err) {
        self.form.classList.remove('eq-form--busy');
        Array.prototype.forEach.call(self.form.querySelectorAll('.eq-submit'), function (b) { b.disabled = false; });
        if (err.problem && err.problem.errors) {
          var errs = {};
          err.problem.errors.forEach(function (x) { errs[x.field] = x.message; });
          self.showErrors(errs);
        }
        self.say(messageFor(err, self.t('submitError', 'Das hat leider nicht geklappt. Bitte versuche es erneut.')), 'error');
      });
      return;
    }

    var body = { token: this.token, lang: this.lang, values: values, website: this.form.querySelector('[name="' + HONEYPOT + '"]').value };
    if (this.quiz) body.answers = this.quiz.answers;

    fetchJson(withLang(self.api + '/forms/' + encodeURIComponent(self.slug) + '/submissions', self.lang), {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    }).then(function (result) {
      if (result.runToken) {
        // Background steps (PDF, webhook) - BEFORE onSuccess and with keepalive so that the request survives
        // a completion redirect. Failures are the concern of the admin and housekeeping, not of the visitor.
        fetchJson(withLang(self.api + '/submissions/' + encodeURIComponent(result.submissionId) + '/run', self.lang), {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token: result.runToken }), keepalive: true
        }).catch(function () { /* deliberately quiet */ });
      }
      self.onSuccess(result);
    }).catch(function (err) {
      self.form.classList.remove('eq-form--busy');
      Array.prototype.forEach.call(self.form.querySelectorAll('.eq-submit'), function (b) { b.disabled = false; });
      if (err.problem && err.problem.errors) {
        var errs = {};
        err.problem.errors.forEach(function (x) { errs[x.field] = x.message; });
        self.showErrors(errs);
      }
      if (err.problem && (err.problem.errorCode === 'security.token_expired' || err.problem.errorCode === 'security.token_replayed')) {
        self.token = null; // fetch a new token so that the next attempt works
        fetchJson(withLang(self.api + '/forms/' + encodeURIComponent(self.slug) + '/token', self.lang), { cache: 'no-store' }).then(function (t) { self.token = t.token; });
      }
      self.say(messageFor(err, self.t('submitError', 'Das hat leider nicht geklappt. Bitte versuche es erneut.')), 'error');
    });
  };

  FormWidget.prototype.onSuccess = function (result) {
    var c = result.completion || {};
    if (c.mode === 'redirect' && c.url) { window.location.href = c.url; return; }
    var keep = this.form.querySelector('.eq-form__intro');
    this.form.innerHTML = '';
    if (keep) this.form.appendChild(keep);
    if (result.quiz) {
      var box = el('div', { 'class': 'eq-quiz__result', role: 'status' });
      box.appendChild(el('h3', { 'class': 'eq-quiz__result-title' }, result.quiz.title));
      box.appendChild(el('div', { 'class': 'eq-quiz__result-body' }, result.quiz.body));
      if (result.quiz.findings && result.quiz.findings.length) {
        var list = el('ul', { 'class': 'eq-quiz__findings' });
        result.quiz.findings.forEach(function (f) { list.appendChild(el('li', { 'class': 'eq-quiz__finding' }, f)); });
        box.appendChild(list);
      }
      this.form.appendChild(box);
    }
    if (c.message) this.form.appendChild(el('div', { 'class': 'eq-message eq-message--success', role: 'status' }, c.message));
    this.form.classList.remove('eq-form--busy');
    this.form.classList.add('eq-form--done');
    this.host.dispatchEvent(new CustomEvent('entriqa:submitted', { bubbles: true, detail: { slug: this.slug, submissionId: result.submissionId, quiz: result.quiz || null } }));
  };

  // #21: the test protocol. Deliberately NOT eq-* classes - this is an admin-only view, not part of the
  // theme markup contract (check-eq-classes.mjs guards eq-* only). Steps: [{stepKey, statusName, error, notes:[{label,value}]}].
  FormWidget.prototype.renderTestReport = function (report) {
    var keep = this.form.querySelector('.eq-form__intro');
    this.form.innerHTML = '';
    if (keep) this.form.appendChild(keep);
    this.form.classList.remove('eq-form--busy');
    this.form.classList.add('eq-form--done');

    var box = el('div', { 'class': 'eqtest-report', role: 'status' });
    box.appendChild(el('h3', { 'class': 'eqtest-report__title' }, this.t('testReportTitle', 'Testprotokoll')));
    if (report && report.mailTo) box.appendChild(el('p', { 'class': 'eqtest-report__mailto' }, this.t('testMailTo', 'Testmails an:') + ' ' + report.mailTo));

    var list = el('ul', { 'class': 'eqtest-steps' });
    (report && report.steps || []).forEach(function (s) {
      var name = (s.statusName || '').toLowerCase();
      var li = el('li', { 'class': 'eqtest-step eqtest-step--' + name });
      li.appendChild(el('span', { 'class': 'eqtest-step__key' }, s.stepKey));
      li.appendChild(el('span', { 'class': 'eqtest-step__status' }, s.statusName || ''));
      if (s.error) li.appendChild(el('div', { 'class': 'eqtest-step__error' }, s.error));
      if (s.notes && s.notes.length) {
        var nl = el('ul', { 'class': 'eqtest-notes' });
        s.notes.forEach(function (n) { nl.appendChild(el('li', { 'class': 'eqtest-note' }, n.label + ': ' + n.value)); });
        li.appendChild(nl);
      }
      list.appendChild(li);
    });
    box.appendChild(list);
    this.form.appendChild(box);
    this.host.dispatchEvent(new CustomEvent('entriqa:tested', { bubbles: true, detail: { slug: this.slug } }));
  };

  FormWidget.prototype.say = function (text, kind) {
    this.message.textContent = text;
    this.message.className = 'eq-message eq-message--' + kind;
    this.message.hidden = false;
  };

  /* ---------- Helfer ---------- */

  function hiddenValue(f) {
    var s = f.source || 'utm_source';
    if (s.indexOf('fixed:') === 0) return s.slice(6);
    if (s === 'referrer') return document.referrer.slice(0, 200);
    if (s === 'page') return (window.location.origin + window.location.pathname).slice(0, 200);
    if (s.indexOf('query:') === 0) s = s.slice(6);
    // UTM and friends always fresh from the current URL - deliberately no sessionStorage (banner free, § 25 TDDDG).
    try { return (new URLSearchParams(window.location.search).get(s) || '').slice(0, 200); } catch (e) { return ''; }
  }

  function autocompleteFor(f) {
    var l = (f.label || '').toLowerCase();
    if (f.type === 'email') return 'email';
    if (f.type !== 'text') return null;
    if (l.indexOf('vorname') >= 0) return 'given-name';
    if (l.indexOf('nachname') >= 0) return 'family-name';
    if (l === 'name') return 'name';
    if (l.indexOf('unternehmen') >= 0 || l.indexOf('firma') >= 0) return 'organization';
    if (l.indexOf('telefon') >= 0) return 'tel';
    return null;
  }

  // The server answers errors in the language it is asked for; without this the visitor would get
  // the field errors in the form language and the error title in the browser language.
  function withLang(url, lang) {
    return url + (url.indexOf('?') < 0 ? '?' : '&') + 'lang=' + encodeURIComponent(lang);
  }

  function fetchJson(url, opts) {
    return fetch(url, opts).then(function (res) {
      if (res.status === 202 || res.status === 204) return {};
      return res.text().then(function (text) {
        var data = null;
        try { data = text ? JSON.parse(text) : null; } catch (e) { /* no JSON */ }
        if (!res.ok) { var err = new Error('HTTP ' + res.status); err.status = res.status; err.problem = data; throw err; }
        return data;
      });
    });
  }

  function messageFor(err, fallback) {
    if (err && err.problem && err.problem.title && err.problem.errorCode !== 'forms.validation') return err.problem.title;
    return fallback;
  }

  function el(tag, attrs, children) {
    var n = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      var v = attrs[k];
      if (v === null || v === undefined || v === false) return;
      if (v === true) { n.setAttribute(k, ''); return; }
      n.setAttribute(k, v);
    });
    if (children !== undefined) {
      (Array.isArray(children) ? children : [children]).forEach(function (c) {
        if (c === null || c === undefined) return;
        n.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
      });
    }
    return n;
  }

  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
  window.eqForms = { init: init };
})();
