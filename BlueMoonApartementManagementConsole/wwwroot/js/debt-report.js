(() => {
    // Báo cáo thu và công nợ: lọc/phân trang server-side, xuất Excel bằng fetch -> blob để bắt được lỗi 400.
    const rootId = 'debtReport';
    let bound = false;
    let mountedRoot = null;
    let rootObserver = null;
    let periodsLoadPromise = null;
    let selectedRow = null;

    const el = (id) => document.getElementById(id);
    const inRoot = (node) => Boolean(node && document.getElementById(rootId)?.contains(node));
    const filterForm = () => el('drFilter');
    const field = (name) => filterForm()?.elements.namedItem(name)?.value?.trim() ?? '';

    const showAlert = (message, success = false) => {
        const box = el('drAlert');
        if (!box) return; // callback có thể chạy sau khi đã rời trang
        box.textContent = message;
        box.classList.toggle('alert-success', success);
        box.hidden = false;
        box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    };

    const messageFor = (response, data, fallback) => {
        const errors = data?.errors ? Object.values(data.errors).flat().join(' ') : null;
        return data?.message ?? errors ?? data?.title
            ?? (response.status === 403 ? 'Tài khoản hiện tại không có quyền xem báo cáo.'
                : response.status >= 500 ? `Máy chủ gặp lỗi (${response.status}).`
                    : `${fallback} (HTTP ${response.status}).`);
    };

    const api = async (url) => {
        const response = await fetch(url, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        });
        if (response.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }
        let data = null;
        try { data = await response.json(); } catch { /* không phải JSON */ }
        return { ok: response.ok, status: response.status, data };
    };

    const money = (value) => Number(value ?? 0).toLocaleString('vi-VN');

    const statusMeta = () => Object.fromEntries(
        [...el('drStatus').options]
            .filter((o) => o.value)
            .map((o) => [o.value, { label: o.textContent.trim(), badge: badgeOf(o.value) }]));

    const badgeOf = (status) => ({
        ChuaNop: 'badge badge-muted',
        NopMotPhan: 'badge badge-warn',
        DaNopDu: 'badge badge-ok'
    })[status] ?? 'badge';

    const cell = (text, className) => {
        const td = document.createElement('td');
        td.textContent = text;
        if (className) td.className = className;
        return td;
    };

    const params = (includePaging, page) => {
        const result = new URLSearchParams();
        if (field('kyThu')) result.set('kyThu', field('kyThu'));
        if (field('loaiKhoan')) result.set('loaiKhoan', field('loaiKhoan'));
        if (field('trangThai')) result.set('trangThai', field('trangThai'));
        if (field('ho')) result.set('ho', field('ho'));
        if (includePaging) {
            result.set('page', page);
            result.set('pageSize', el('drPageSize').value);
        }
        return result;
    };

    const clearSelection = () => {
        selectedRow = null;
        const remind = el('drRemind');
        if (remind) remind.disabled = true;
    };

    const renderRow = (meta) => (item) => {
        const tr = document.createElement('tr');

        const choose = document.createElement('td');
        const radio = document.createElement('input');
        radio.type = 'radio';
        radio.name = 'drHousehold';
        radio.value = String(item.householdId);
        radio.dataset.periodId = String(field('kyThu'));
        radio.dataset.householdId = String(item.householdId);
        radio.dataset.householdNumber = item.householdNumber;
        radio.setAttribute('aria-label', `Chọn hộ ${item.householdNumber}`);
        radio.addEventListener('change', () => {
            if (!radio.checked) return;
            selectedRow = radio.dataset;
            el('drRemind').disabled = false;
        });
        choose.appendChild(radio);
        tr.appendChild(choose);

        tr.appendChild(cell(item.apartmentCode, 'mono'));
        tr.appendChild(cell(item.buildingName));
        tr.appendChild(cell(item.householdNumber));
        tr.appendChild(cell(item.headName));
        tr.appendChild(cell(money(item.mustPay), 'num'));
        tr.appendChild(cell(money(item.paid), 'num'));
        tr.appendChild(cell(money(item.remaining), 'num rp-remaining'));
        const status = meta[item.status] ?? { label: item.status, badge: 'badge' };
        const statusCell = document.createElement('td');
        const badge = document.createElement('span');
        badge.className = status.badge;
        badge.textContent = status.label;
        statusCell.appendChild(badge);
        tr.appendChild(statusCell);

        const action = document.createElement('td');
        const link = document.createElement('button');
        link.type = 'button';
        link.className = 'btn-link';
        link.textContent = 'Nhắc nộp';
        link.dataset.action = 'remind-row';
        link.dataset.periodId = String(field('kyThu'));
        link.dataset.householdId = String(item.householdId);
        link.dataset.householdNumber = item.householdNumber;
        action.appendChild(link);
        tr.appendChild(action);
        return tr;
    };

    const render = (result) => {
        el('drLoading').hidden = true;
        const summary = result.summary;
        el('drPeriodLabel').textContent = `Kỳ báo cáo: ${result.periodLabel}`;
        el('drMustPay').textContent = money(summary.mustPay);
        el('drPaid').textContent = money(summary.paid);
        el('drRemaining').textContent = money(summary.remaining);
        el('drOwedHouseholds').textContent = String(summary.owedHouseholds);

        const rows = result.rows.items ?? [];
        const total = result.rows.total;
        const pageSize = result.rows.pageSize;
        const page = result.rows.page;
        const totalPages = Math.max(1, Math.ceil(total / pageSize));

        el('drBody').replaceChildren(...rows.map(renderRow(statusMeta())));
        el('drEmpty').hidden = total > 0;
        el('drTable').hidden = total === 0;
        el('drPager').hidden = total === 0;
        el('drPageInfo').textContent = `Trang ${page} / ${totalPages} (${total} bản ghi)`;
        clearSelection();

        const prev = el('drPrev');
        const next = el('drNext');
        prev.disabled = page <= 1;
        prev.dataset.page = String(page - 1);
        next.disabled = page >= totalPages;
        next.dataset.page = String(page + 1);
    };

    const load = async (page = 1) => {
        if (!field('kyThu')) {
            showAlert('Vui chọn kỳ thu cần xem báo cáo.');
            return;
        }
        try {
            const response = await api(`/api/bao-cao/thu-cong-no?${params(true, page)}`);
            if (response.status === 401) return;
            if (!response.ok) {
                el('drLoading').hidden = true;
                showAlert(messageFor(response, response.data, 'Không tải được báo cáo công nợ.'));
                return;
            }
            el('drAlert').hidden = true;
            render(response.data);
        } catch {
            el('drLoading').hidden = true;
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.');
        }
    };

    // Enhanced navigation của Blazor có thể tái dùng node <section> nên không được dùng "đã tải cho root"
    // làm guard: khi quay lại trang, nội dung select đã bị reset nhưng node vẫn giữ nguyên. Mỗi lần init()
    // đều tải lại; promise dùng chung để tránh gọi API chồng nhau. Nếu DOM đổi trong lúc fetch thì tự retry.
    const fetchAndFillPeriods = async (expectedRoot, attempt = 0) => {
        const response = await api('/api/ky-thu');
        if (response.status === 401 || !expectedRoot) return false;
        if (mountedRoot !== expectedRoot) {
            return attempt < 2 && el(rootId) ? fetchAndFillPeriods(mountedRoot, attempt + 1) : false;
        }
        if (!response.ok || !Array.isArray(response.data))
            throw new Error(messageFor(response, response.data, 'Không tải được danh sách kỳ thu.'));

        const select = filterForm()?.elements.namedItem('kyThu');
        if (!select) return false;
        const previous = select.value;
        const options = [new Option('Chọn kỳ thu', '')];
        response.data.forEach((p) => options.push(
            new Option(`Tháng ${String(p.month).padStart(2, '0')}/${p.year}`, String(p.id))));
        select.replaceChildren(...options);
        if (options.some((o) => o.value === previous)) select.value = previous;
        return true;
    };

    const loadPeriods = async () => {
        if (periodsLoadPromise) return periodsLoadPromise;
        periodsLoadPromise = fetchAndFillPeriods(mountedRoot)
            .catch((error) => {
                showAlert(error.message);
                return false;
            })
            .finally(() => { periodsLoadPromise = null; });
        return periodsLoadPromise;
    };

    const openReminders = (periodId, householdId, householdNumber) => {
        if (!periodId || !householdId) return;
        const query = new URLSearchParams({ kyThu: periodId, hoGiaDinh: householdId });
        if (householdNumber) query.set('ho', householdNumber);
        location.assign(`/cong-no?${query}`);
    };

    const download = async () => {
        if (!field('kyThu')) {
            showAlert('Vui chọn kỳ thu cần xuất báo cáo.');
            return;
        }
        const button = el('drExport');
        button.disabled = true;
        const previous = button.textContent;
        button.textContent = 'Đang xuất...';
        try {
            const response = await fetch(`/api/bao-cao/thu-cong-no/excel?${params(false, 1)}`, {
                credentials: 'same-origin',
                headers: { Accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }
            });
            if (response.status === 401) {
                location.assign('/login');
                return;
            }
            if (!response.ok) {
                let data = null;
                try { data = await response.json(); } catch { /* bỏ qua */ }
                showAlert(messageFor(response, data, 'Không xuất được file Excel.'));
                return;
            }

            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            const disposition = response.headers.get('content-disposition') ?? '';
            const match = disposition.match(/filename="?([^";]+)"?/i);
            link.download = match?.[1] ?? 'bao-cao-thu-cong-no.xlsx';
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            showAlert('Đã xuất file Excel báo cáo thu và công nợ.', true);
        } catch {
            showAlert('Không xuất được file Excel. Vui lòng thử lại.');
        } finally {
            button.disabled = false;
            button.textContent = previous;
        }
    };

    const onClick = (event) => {
        if (!inRoot(event.target)) return;
        if (event.target.closest?.('#drExport')) {
            void download();
            return;
        }
        if (event.target.closest?.('#drReset')) {
            filterForm().reset();
            el('drPageSize').value = '10';
            el('drAlert').hidden = true;
            el('drContent').hidden = false;
            clearSelection();
            // "Xóa lọc" cũng là nút thử lại khi lần đầu tải kỳ thất bại
            void loadPeriods().then((ok) => {
                if (!ok) return;
                updatePeriodState();
                void load(1);
            });
            return;
        }
        const pagerButton = event.target.closest?.('#drPrev, #drNext');
        if (pagerButton && !pagerButton.disabled) {
            void load(Number(pagerButton.dataset.page));
            return;
        }
        const remind = event.target.closest?.('[data-action="remind-row"]');
        if (remind) {
            openReminders(remind.dataset.periodId, remind.dataset.householdId, remind.dataset.householdNumber);
            return;
        }
        if (event.target.closest?.('#drRemind') && selectedRow) {
            openReminders(selectedRow.periodId, selectedRow.householdId, selectedRow.householdNumber);
        }
    };

    const onSubmit = (event) => {
        if (event.target.id !== 'drFilter') return;
        event.preventDefault();
        void load(1);
    };

    const onChange = (event) => {
        if (!inRoot(event.target)) return;
        if (event.target === filterForm().elements.namedItem('kyThu')) {
            el('drContent').hidden = false;
            void load(1);
        }
        if (event.target.id === 'drPageSize') void load(1);
    };

    const updatePeriodState = () => {
        const empty = el('drEmptyPeriod');
        const content = el('drContent');
        if (!empty || !content) return; // đã rời trang trong lúc fetch
        const hasPeriod = Boolean(filterForm()?.elements.namedItem('kyThu')?.options.length > 1);
        empty.hidden = hasPeriod;
        content.hidden = !hasPeriod;
    };

    const init = () => {
        const root = el(rootId);
        if (!root) return;
        if (root !== mountedRoot) {
            mountedRoot = root;
            clearSelection();
        }
        if (!bound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('change', onChange);
            bound = true;
        }
        // Luôn tải lại: Blazor enhanced navigation có thể giữ nguyên node <section> mà reset option của select.
        void loadPeriods().then((ok) => { if (ok) updatePeriodState(); });
    };

    const observeRootChanges = () => {
        if (rootObserver) return;
        rootObserver = new MutationObserver(() => {
            const current = el(rootId);
            if (current && current !== mountedRoot) init();
        });
        rootObserver.observe(document.documentElement, { childList: true, subtree: true });
    };

    const bindEnhancedLoad = () => {
        if (!window.Blazor?.addEventListener) return false;
        window.Blazor.addEventListener('enhancedload', init);
        return true;
    };

    init();
    // blazor.web.js có thể định nghĩa window.Blazor sau khi script này chạy; dự phòng thêm load + observer.
    if (!bindEnhancedLoad()) {
        window.addEventListener('load', () => { bindEnhancedLoad(); init(); }, { once: true });
    }
    // Bao gồm cả trường hợp quay lại từ bfcache của trình duyệt.
    window.addEventListener('pageshow', init);
    observeRootChanges();
})();
