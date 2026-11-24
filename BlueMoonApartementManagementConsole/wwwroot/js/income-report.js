(() => {
    // Báo cáo thu chi: gom theo ngày xác nhận thu ở server; không vẽ biểu đồ.
    const rootId = 'incomeReport';
    let bound = false;
    let mountedRoot = null;
    let rootObserver = null;

    const el = (id) => document.getElementById(id);
    const inRoot = (node) => Boolean(node && document.getElementById(rootId)?.contains(node));
    const filterForm = () => el('irFilter');
    const field = (name) => filterForm()?.elements.namedItem(name)?.value?.trim() ?? '';

    const showAlert = (message, success = false) => {
        const box = el('irAlert');
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
    const localToday = () => {
        const now = new Date();
        return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    };
    const displayDate = (value) => {
        if (!value) return '';
        const [year, month, day] = value.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    };

    const params = (includePaging, page) => {
        const result = new URLSearchParams();
        if (field('ky')) result.set('ky', field('ky'));
        if (field('moc')) result.set('moc', field('moc'));
        if (field('from')) result.set('from', field('from'));
        if (field('to')) result.set('to', field('to'));
        if (includePaging) {
            result.set('page', page);
            result.set('pageSize', el('irPageSize').value);
        }
        return result;
    };

    const cell = (text, className) => {
        const td = document.createElement('td');
        td.textContent = text;
        if (className) td.className = className;
        return td;
    };

    const renderHead = (result) => {
        const headers = ['Kỳ', 'Từ ngày', 'Đến ngày'];
        headers.push(...result.feeTypes.map((type) => result.feeTypeLabels[type] ?? type));
        headers.push('Tổng thu (đ)');
        el('irHead').replaceChildren(...headers.map((text) => {
            const th = document.createElement('th');
            th.textContent = text;
            if (text.startsWith('Tổng') || result.feeTypes.some((t) => (result.feeTypeLabels[t] ?? t) === text))
                th.className = 'num';
            return th;
        }));
    };

    const renderRow = (feeTypes) => (item) => {
        const tr = document.createElement('tr');
        tr.appendChild(cell(item.label));
        tr.appendChild(cell(displayDate(item.from)));
        tr.appendChild(cell(displayDate(item.to)));
        feeTypes.forEach((type) => tr.appendChild(cell(money(item.byFeeType[type]), 'num')));
        tr.appendChild(cell(money(item.total), 'num rp-remaining'));
        return tr;
    };

    const render = (result) => {
        el('irLoading').hidden = true;
        el('irRange').textContent = `Khoảng báo cáo: từ ${displayDate(result.from)} đến ${displayDate(result.to)}`;
        el('irKyLabel').textContent = result.kyLabel;
        el('irTotal').textContent = money(result.total);
        el('irPeriodCount').textContent = String(result.rows.total);

        renderHead(result);
        const rows = result.rows.items ?? [];
        el('irBody').replaceChildren(...rows.map(renderRow(result.feeTypes)));
        el('irEmpty').hidden = result.rows.total > 0;
        el('irContent').hidden = result.rows.total === 0;
        el('irPager').hidden = result.rows.total === 0;

        const page = result.rows.page;
        const totalPages = Math.max(1, Math.ceil(result.rows.total / result.rows.pageSize));
        el('irPageInfo').textContent = `Trang ${page} / ${totalPages} (${result.rows.total} kỳ)`;
        const prev = el('irPrev');
        const next = el('irNext');
        prev.disabled = page <= 1;
        prev.dataset.page = String(page - 1);
        next.disabled = page >= totalPages;
        next.dataset.page = String(page + 1);
    };

    const load = async (page = 1) => {
        try {
            const response = await api(`/api/bao-cao/thu-chi?${params(true, page)}`);
            if (response.status === 401) return;
            if (!response.ok) {
                el('irLoading').hidden = true;
                el('irContent').hidden = true;
                showAlert(messageFor(response, response.data, 'Không tải được báo cáo thu chi.'));
                return;
            }
            el('irAlert').hidden = true;
            render(response.data);
        } catch {
            el('irLoading').hidden = true;
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.');
        }
    };

    const download = async () => {
        const button = el('irExport');
        button.disabled = true;
        const previous = button.textContent;
        button.textContent = 'Đang xuất...';
        try {
            const response = await fetch(`/api/bao-cao/thu-chi/excel?${params(false, 1)}`, {
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
            link.download = match?.[1] ?? 'bao-cao-thu-chi.xlsx';
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            showAlert('Đã xuất file Excel báo cáo thu chi.', true);
        } catch {
            showAlert('Không xuất được file Excel. Vui lòng thử lại.');
        } finally {
            button.disabled = false;
            button.textContent = previous;
        }
    };

    const onClick = (event) => {
        if (!inRoot(event.target)) return;
        if (event.target.closest?.('#irExport')) {
            void download();
            return;
        }
        if (event.target.closest?.('#irReset')) {
            const today = localToday();
            filterForm().reset();
            filterForm().elements.namedItem('ky').value = 'Thang';
            filterForm().elements.namedItem('moc').value = today;
            el('irPageSize').value = '10';
            el('irAlert').hidden = true;
            void load(1);
            return;
        }
        const pagerButton = event.target.closest?.('#irPrev, #irNext');
        if (pagerButton && !pagerButton.disabled) void load(Number(pagerButton.dataset.page));
    };

    const onSubmit = (event) => {
        if (event.target.id !== 'irFilter') return;
        event.preventDefault();
        void load(1);
    };

    const onChange = (event) => {
        if (!inRoot(event.target)) return;
        if (['ky', 'moc', 'from', 'to'].includes(event.target.name)) void load(1);
        if (event.target.id === 'irPageSize') void load(1);
    };

    const init = () => {
        const root = el(rootId);
        if (!root) return;
        if (root !== mountedRoot) {
            mountedRoot = root;
            const kySelect = filterForm().elements.namedItem('ky');
            const mocInput = filterForm().elements.namedItem('moc');
            if (!kySelect.value) kySelect.value = 'Thang';
            if (!mocInput.value) mocInput.value = localToday();
        }
        if (!bound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('change', onChange);
            bound = true;
        }
        void load(1);
    };

    const observeRootChanges = () => {
        if (rootObserver) return;
        rootObserver = new MutationObserver(() => {
            const current = el(rootId);
            if (current && current !== mountedRoot) init();
        });
        rootObserver.observe(document.documentElement, { childList: true, subtree: true });
    };

    init();
    if (window.Blazor?.addEventListener) {
        window.Blazor.addEventListener('enhancedload', init);
    }
    observeRootChanges();
})();
