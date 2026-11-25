(function () {
    'use strict';

    var TYPE_LABELS = { BatBuoc: 'Bắt buộc', TuNguyen: 'Tự nguyện', BaoTriRieng: 'Bảo trì riêng' };
    var SCOPE_LABELS = { Chung: 'Toàn chung cư', ChiDinh: 'Chỉ định hộ' };
    var CYCLE_LABELS = { MotLan: 'Một lần', Thang: 'Hàng tháng', Nam: 'Hàng năm' };
    var BASIS_LABELS = { DienTich: 'Diện tích', NhanKhau: 'Nhân khẩu', Ho: 'Theo hộ', CoDinh: 'Cố định' };
    var RATE_MODE_LABELS = { Chung: 'Chung', TheoLoai: 'Theo loại' };
    var APT_TYPES = ['CanHo', 'Kiot', 'Penthouse'];
    var APT_LABELS = { CanHo: 'Căn hộ ở', Kiot: 'Kiot', Penthouse: 'Penthouse' };

    var items = [];
    var canManage = false;
    var page = 1;
    var pageSize = 10;

    function byId(id) { return document.getElementById(id); }

    function pageRoot() { return byId('fee-items-page'); }

    function todayIso() {
        var d = new Date();
        var m = String(d.getMonth() + 1).padStart(2, '0');
        var day = String(d.getDate()).padStart(2, '0');
        return d.getFullYear() + '-' + m + '-' + day;
    }

    function formatMoney(value) {
        return Number(value).toLocaleString('vi-VN');
    }

    function formatDate(iso) {
        if (!iso) return '';
        var p = iso.split('-');
        return p[2] + '/' + p[1] + '/' + p[0];
    }

    // Đơn giá đang có hiệu lực hôm nay. Server trả danh sách giảm dần theo ngày bắt đầu.
    function currentRate(item) {
        var today = todayIso();
        for (var i = 0; i < item.rates.length; i++) {
            var r = item.rates[i];
            if (r.effectiveFrom <= today && (!r.effectiveTo || r.effectiveTo > today)) return r;
        }
        return null;
    }

    // Giá theo loại căn đang có hiệu lực hôm nay (dùng để hiển thị danh sách và lịch sử).
    function currentTypeRate(item, type) {
        var today = todayIso();
        var rates = item.typeRates || [];
        for (var i = 0; i < rates.length; i++) {
            var r = rates[i];
            if (r.apartmentType === type && r.effectiveFrom <= today && (!r.effectiveTo || r.effectiveTo > today)) return r;
        }
        return null;
    }

    // Chỉ hiện ba ô giá khi khoản tính theo diện tích và chọn chế độ TheoLoai.
    function syncRateModeFields(form) {
        if (!form) return;
        var modeEl = form.elements.namedItem('rateMode');
        var basisEl = form.elements.namedItem('calculationBasis');
        var mode = modeEl ? modeEl.value : 'Chung';
        var basis = basisEl ? basisEl.value : (form.dataset.basis || 'DienTich');
        var typed = mode === 'TheoLoai' && basis === 'DienTich';

        var group = form.querySelector('.fee-type-rate-group') || byId('feeTypeRates');
        if (group) group.hidden = !typed;
        var commonLabel = form.querySelector('.fee-common-rate-label') || byId('feeCommonRateLabel');
        if (commonLabel) commonLabel.hidden = typed;
        var commonInput = form.elements.namedItem('rate');
        if (commonInput) commonInput.required = !typed;
        APT_TYPES.forEach(function (type) {
            var input = form.elements.namedItem('rate_' + type);
            if (input) input.required = typed;
        });
    }

    async function api(path, options) {
        var opts = Object.assign({ credentials: 'same-origin', headers: { 'Accept': 'application/json' } }, options || {});
        if (opts.body !== undefined) {
            opts.headers = Object.assign({ 'Content-Type': 'application/json' }, opts.headers);
            opts.body = JSON.stringify(opts.body);
        }

        var res = await fetch(path, opts);
        if (res.status === 401) {
            window.location.href = '/login';
            return { ok: false, status: 401, body: null };
        }

        var body = null;
        try { body = await res.json(); } catch (e) { body = null; }

        if (res.status === 403 && body && body.code === 'PASSWORD_CHANGE_REQUIRED') {
            window.location.href = '/ho-so';
            return { ok: false, status: 403, body: body };
        }
        return { ok: res.ok, status: res.status, body: body };
    }

    function errorText(result) {
        if (result.body && result.body.message) return result.body.message;
        if (result.body && result.body.errors) {
            return Object.keys(result.body.errors)
                .map(function (k) { return result.body.errors[k].join(' '); })
                .join(' ');
        }
        return 'Lỗi ' + result.status + (result.body && result.body.title ? ': ' + result.body.title : '');
    }

    function showMessage(text, kind) {
        var box = byId('fee-message');
        if (!box) return;
        box.textContent = text;
        box.className = 'alert ' + (kind === 'success' ? 'alert-success' : 'alert-warn');
        box.hidden = false;
    }

    // ---- Tải và hiển thị ----

    async function load() {
        var result = await api('/api/khoan-thu');
        if (!result.ok) {
            showMessage(errorText(result), 'warn');
            return;
        }
        items = result.body || [];
        canManage = !!byId('fee-create-section');
        render();
    }

    function applyFilters(list) {
        var type = byId('fee-filter-type') ? byId('fee-filter-type').value : '';
        var text = byId('fee-filter-text') ? byId('fee-filter-text').value.trim().toLowerCase() : '';
        return list.filter(function (item) {
            if (type && item.feeType !== type) return false;
            if (text && item.code.toLowerCase().indexOf(text) < 0 && item.name.toLowerCase().indexOf(text) < 0) return false;
            return true;
        });
    }

    function render() {
        var body = byId('fee-items-body');
        var empty = byId('fee-empty');
        if (!body) return;

        var filtered = applyFilters(items);
        // Enhanced navigation có thể thay select bằng bản SSR (mặc định 10) trong khi JS giữ giá trị cũ.
        var sizeSelect = byId('feePageSize');
        if (sizeSelect) pageSize = Number(sizeSelect.value) || 10;
        var total = filtered.length;
        var totalPages = Math.max(1, Math.ceil(total / pageSize));
        if (page > totalPages) page = totalPages;
        var start = (page - 1) * pageSize;
        var shown = filtered.slice(start, start + pageSize);

        body.textContent = '';
        shown.forEach(function (item) {
            body.appendChild(buildRow(item));
            body.appendChild(buildDetailRow(item));
        });
        if (empty) empty.hidden = total > 0;

        var pager = byId('feePager');
        if (pager) pager.hidden = total === 0;
        var info = byId('feePageInfo');
        if (info) info.textContent = 'Trang ' + page + ' / ' + totalPages + ' (' + total + ' bản ghi)';
        var prev = byId('feePrev');
        if (prev) {
            prev.disabled = page <= 1;
            prev.dataset.page = String(page - 1);
        }
        var next = byId('feeNext');
        if (next) {
            next.disabled = page >= totalPages;
            next.dataset.page = String(page + 1);
        }
    }

    function cell(text) {
        var td = document.createElement('td');
        td.textContent = text;
        return td;
    }

    function actionButton(label, action, id) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'btn-link';
        b.textContent = label;
        b.dataset.action = action;
        b.dataset.id = String(id);
        return b;
    }

    function buildRow(item) {
        var tr = document.createElement('tr');
        var rate = currentRate(item);

        tr.appendChild(cell(item.code));
        tr.appendChild(cell(item.name));
        tr.appendChild(cell(TYPE_LABELS[item.feeType] || item.feeType));
        tr.appendChild(cell(SCOPE_LABELS[item.feeScope] || item.feeScope));
        tr.appendChild(cell(CYCLE_LABELS[item.feeCycle] || item.feeCycle));
        tr.appendChild(cell(BASIS_LABELS[item.calculationBasis] || item.calculationBasis));
        tr.appendChild(cell(RATE_MODE_LABELS[item.rateMode] || item.rateMode || 'Chung'));
        tr.appendChild(cell(item.unit));
        if (item.rateMode === 'TheoLoai') {
            var rates = APT_TYPES.map(function (type) {
                var r = currentTypeRate(item, type);
                return APT_LABELS[type] + ': ' + (r ? formatMoney(r.rate) : 'Chưa có');
            }).join(' · ');
            tr.appendChild(cell(rates + ' đ / ' + item.unit));
        } else {
            tr.appendChild(cell(rate ? formatMoney(rate.rate) + ' đ / ' + item.unit : 'Chưa có'));
        }

        var actions = document.createElement('td');
        actions.className = 'row-actions';
        actions.appendChild(actionButton('Lịch sử', 'history', item.id));
        if (canManage) actions.appendChild(actionButton('Đổi đơn giá', 'rate', item.id));
        tr.appendChild(actions);

        return tr;
    }

    function buildDetailRow(item) {
        var tr = document.createElement('tr');
        tr.className = 'detail-row';
        tr.dataset.detailFor = String(item.id);
        tr.hidden = true;

        var td = document.createElement('td');
        td.colSpan = 10;
        td.appendChild(buildHistory(item));
        if (canManage) td.appendChild(buildRateForm(item));

        tr.appendChild(td);
        return tr;
    }

    function buildHistory(item) {
        var wrap = document.createElement('div');
        wrap.className = 'rate-history';
        wrap.hidden = true;

        var lookupTitle = document.createElement('p');
        lookupTitle.textContent = 'Tra cứu đơn giá theo ngày';
        wrap.appendChild(lookupTitle);

        var dateInput = input('date', 'lookupDate', { value: todayIso() });
        dateInput.className = 'rate-lookup-date';
        var lookupBtn = actionButton('Tra cứu', 'lookup', item.id);
        lookupBtn.className = 'btn-approve';

        var lookupRow = document.createElement('div');
        lookupRow.className = 'rate-lookup';
        lookupRow.appendChild(labeled('Ngày', dateInput));
        lookupRow.appendChild(lookupBtn);
        wrap.appendChild(lookupRow);

        var out = document.createElement('p');
        out.className = 'rate-lookup-result muted';
        wrap.appendChild(out);

        var historyTitle = document.createElement('p');
        historyTitle.textContent = 'Lịch sử đơn giá';
        wrap.appendChild(historyTitle);

        var ul = document.createElement('ul');
        var addHistory = function (label, r) {
            var li = document.createElement('li');
            if (r) {
                var end = r.effectiveTo ? formatDate(r.effectiveTo) : 'nay';
                li.textContent = label + ': ' + formatMoney(r.rate) + ' đ, từ ' + formatDate(r.effectiveFrom) + ' đến ' + end;
            } else {
                li.textContent = label + ': Chưa có đơn giá';
            }
            ul.appendChild(li);
        };
        if (item.rateMode === 'TheoLoai') {
            APT_TYPES.forEach(function (type) {
                (item.typeRates || []).filter(function (r) { return r.apartmentType === type; })
                    .forEach(function (r) { addHistory(APT_LABELS[type], r); });
                if (!(item.typeRates || []).some(function (r) { return r.apartmentType === type; })) {
                    addHistory(APT_LABELS[type], null);
                }
            });
        } else {
            item.rates.forEach(function (r) { addHistory('Giá chung', r); });
        }
        wrap.appendChild(ul);
        return wrap;
    }

    function labeled(text, input) {
        var label = document.createElement('label');
        label.textContent = text;
        label.appendChild(input);
        return label;
    }

    function input(type, name, attrs) {
        var el = document.createElement('input');
        el.type = type;
        el.name = name;
        Object.keys(attrs || {}).forEach(function (k) { el[k] = attrs[k]; });
        return el;
    }

    function select(name, options, value) {
        var el = document.createElement('select');
        el.name = name;
        options.forEach(function (o) {
            var opt = document.createElement('option');
            opt.value = o.value;
            opt.textContent = o.label;
            el.appendChild(opt);
        });
        el.value = value || options[0].value;
        return el;
    }

    function buildRateForm(item) {
        var form = document.createElement('form');
        form.className = 'pw-form rate-form';
        form.dataset.id = String(item.id);
        form.dataset.basis = item.calculationBasis;
        form.hidden = true;

        form.appendChild(labeled('Cách đặt giá', select('rateMode', [
            { value: 'Chung', label: 'Giá chung' },
            { value: 'TheoLoai', label: 'Theo loại căn hộ' }
        ], item.rateMode || 'Chung')));

        var commonLabel = labeled('Đơn giá mới (đ)', input('number', 'rate', { min: '0', step: '1', required: true }));
        commonLabel.className = 'fee-common-rate-label';
        form.appendChild(commonLabel);

        var group = document.createElement('fieldset');
        group.className = 'fee-type-rate-group';
        var legend = document.createElement('legend');
        legend.textContent = 'Đơn giá mới theo loại căn hộ (đ)';
        group.appendChild(legend);
        APT_TYPES.forEach(function (type) {
            group.appendChild(labeled(APT_LABELS[type], input('number', 'rate_' + type, { min: '0', step: '1' })));
        });
        form.appendChild(group);

        form.appendChild(labeled('Hiệu lực từ', input('date', 'effectiveFrom', { min: todayIso(), required: true })));

        var submit = document.createElement('button');
        submit.type = 'submit';
        submit.className = 'btn-approve';
        submit.textContent = 'Lưu đơn giá';
        form.appendChild(submit);

        syncRateModeFields(form);
        return form;
    }

    // ---- Thao tác ----

    async function onCreateSubmit(form) {
        var f = form.elements;
        var body = {
            code: f.namedItem('code').value.trim(),
            name: f.namedItem('name').value.trim(),
            feeType: f.namedItem('feeType').value,
            feeScope: f.namedItem('feeScope').value,
            feeCycle: f.namedItem('feeCycle').value,
            calculationBasis: f.namedItem('calculationBasis').value,
            unit: f.namedItem('unit').value.trim(),
            note: f.namedItem('note').value.trim() || null,
            rateMode: f.namedItem('rateMode').value,
            rate: Number(f.namedItem('rate').value),
            typeRates: f.namedItem('rateMode').value === 'TheoLoai'
                ? APT_TYPES.map(function (type) {
                    return { apartmentType: type, rate: Number(f.namedItem('rate_' + type).value) };
                })
                : [],
            effectiveFrom: f.namedItem('effectiveFrom').value,
            effectiveTo: f.namedItem('effectiveTo').value || null
        };

        var btn = form.querySelector('button[type="submit"]');
        if (btn) btn.disabled = true;
        try {
            var result = await api('/api/khoan-thu', { method: 'POST', body: body });
            console.log('POST khoan-thu', result.status, result.body);
            if (!result.ok) {
                showMessage(errorText(result), 'warn');
                return;
            }
            form.reset();
            syncRateModeFields(form);
            showMessage('Đã tạo khoản thu ' + result.body.code + '.', 'success');
            await load();
        } finally {
            var again = document.querySelector('#fee-create-form button[type="submit"]');
            if (again) again.disabled = false;
        }
    }

    async function onRateSubmit(form) {
        var id = form.dataset.id;
        var f = form.elements;
        var body = {
            rateMode: f.namedItem('rateMode').value,
            rate: Number(f.namedItem('rate').value),
            typeRates: f.namedItem('rateMode').value === 'TheoLoai'
                ? APT_TYPES.map(function (type) {
                    return { apartmentType: type, rate: Number(f.namedItem('rate_' + type).value) };
                })
                : [],
            effectiveFrom: f.namedItem('effectiveFrom').value
        };

        var btn = form.querySelector('button[type="submit"]');
        if (btn) btn.disabled = true;
        try {
            var result = await api('/api/khoan-thu/' + encodeURIComponent(id) + '/don-gia', { method: 'POST', body: body });
            if (!result.ok) {
                showMessage(errorText(result), 'warn');
                return;
            }
            showMessage('Đã cập nhật đơn giá.', 'success');
            await load();
        } finally {
            // Tìm lại nút từ DOM vì load() đã vẽ lại bảng.
            var again = document.querySelector('form.rate-form[data-id="' + id + '"] button[type="submit"]');
            if (again) again.disabled = false;
        }
    }

    async function onLookup(btn) {
        var id = btn.dataset.id;
        var detail = document.querySelector('tr.detail-row[data-detail-for="' + id + '"]');
        if (!detail) return;

        var dateEl = detail.querySelector('.rate-lookup-date');
        var out = detail.querySelector('.rate-lookup-result');
        if (!out) return;
        if (!dateEl || !dateEl.value) {
            out.textContent = 'Chọn ngày cần tra cứu.';
            return;
        }

        var item = items.find(function (x) { return String(x.id) === String(id); });
        var typed = item && item.rateMode === 'TheoLoai';
        var endpoint = typed ? '/don-gia-theo-loai' : '/don-gia';
        var result = await api('/api/khoan-thu/' + encodeURIComponent(id) + endpoint + '?ngay=' + encodeURIComponent(dateEl.value));
        if (!result.ok) {
            out.textContent = errorText(result);
            return;
        }

        var d = result.body;
        var prefix = 'Ngày ' + formatDate(d.date) + ': ';
        var suffix = ' · Cơ sở tính: ' + (BASIS_LABELS[d.calculationBasis] || d.calculationBasis);
        if (typed) {
            var byType = {};
            (d.rates || []).forEach(function (r) { byType[r.apartmentType] = r; });
            out.textContent = prefix + APT_TYPES.map(function (type) {
                var r = byType[type];
                return APT_LABELS[type] + ': ' + (r ? formatMoney(r.rate) + ' đ' : 'Chưa có');
            }).join(' · ') + ' / ' + d.unit + suffix;
            return;
        }
        var end = d.effectiveTo ? formatDate(d.effectiveTo) : 'nay';
        out.textContent = prefix + formatMoney(d.rate) + ' đ / ' + d.unit + suffix
            + ' · Hiệu lực từ ' + formatDate(d.effectiveFrom) + ' đến ' + end;
    }

    function onClick(event) {
        var root = pageRoot();
        if (!root) return;

        var pagerBtn = event.target.closest ? event.target.closest('#feePrev, #feeNext') : null;
        if (pagerBtn && root.contains(pagerBtn)) {
            page = Number(pagerBtn.dataset.page);
            render();
            return;
        }

        var btn = event.target.closest ? event.target.closest('button[data-action]') : null;
        if (!btn || !root.contains(btn)) return;

        if (btn.dataset.action === 'lookup') {
            onLookup(btn);
            return;
        }

        var id = btn.dataset.id;
        var detail = document.querySelector('tr.detail-row[data-detail-for="' + id + '"]');
        if (!detail) return;

        var mode = btn.dataset.action;
        if (!detail.hidden && detail.dataset.mode === mode) {
            detail.hidden = true;
            return;
        }

        detail.dataset.mode = mode;
        detail.hidden = false;
        var history = detail.querySelector('.rate-history');
        var form = detail.querySelector('.rate-form');
        if (history) history.hidden = mode !== 'history';
        if (form) form.hidden = mode !== 'rate';
    }

    function onSubmit(event) {
        var form = event.target;
        var root = pageRoot();
        if (!root || !root.contains(form)) return;

        event.preventDefault();
        if (form.id === 'fee-create-form') {
            onCreateSubmit(form);
        } else if (form.classList.contains('rate-form')) {
            onRateSubmit(form);
        }
    }

    function onFilter(event) {
        if (event.target.name === 'calculationBasis' || event.target.name === 'rateMode') {
            var form = event.target.closest ? event.target.closest('form') : null;
            if (form && (form.id === 'fee-create-form' || form.classList.contains('rate-form'))) {
                syncRateModeFields(form);
            }
        }
        if (event.target.id === 'feePageSize') {
            pageSize = Number(event.target.value);
            page = 1;
            render();
            return;
        }
        if (event.target.id === 'fee-filter-type' || event.target.id === 'fee-filter-text') {
            page = 1;
            render();
        }
    }

    // ---- Khởi tạo (chạy lại sau mỗi lần điều hướng nội bộ) ----

    function bindOnce() {
        if (window.__feeItemsBound) return;
        window.__feeItemsBound = true;
        document.addEventListener('click', onClick);
        document.addEventListener('submit', onSubmit);
        document.addEventListener('input', onFilter);
        document.addEventListener('change', onFilter);
    }

    function init() {
        if (!pageRoot()) return;
        bindOnce();
        syncRateModeFields(byId('fee-create-form'));
        load();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
        window.Blazor.addEventListener('enhancedload', init);
    }
})();
