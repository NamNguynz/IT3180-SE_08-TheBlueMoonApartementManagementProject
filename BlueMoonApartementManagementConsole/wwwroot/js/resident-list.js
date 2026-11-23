(() => {
    // Trang Hộ & nhân khẩu: lọc/phân trang phía server. Cùng mẫu với apartment-list.js:
    // document delegation, mountedRoot, MutationObserver và enhancedload của Blazor.
    const rootId = 'residentList';
    let bound = false;
    let mountedRoot = null;
    let rootObserver = null;

    const el = (id) => document.getElementById(id);
    const inRoot = (node) => Boolean(node && document.getElementById(rootId)?.contains(node));
    const filterForm = () => el('resListFilter');
    const field = (name) => filterForm()?.elements.namedItem(name)?.value?.trim() ?? '';

    const showAlert = (message) => {
        const box = el('resListAlert');
        box.textContent = message;
        box.hidden = false;
    };

    const api = async (url) => {
        const res = await fetch(url, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        });
        if (res.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }
        let data = null;
        try { data = await res.json(); } catch { /* không phải JSON */ }
        return { ok: res.ok, status: res.status, data };
    };

    const buildQuery = (page) => {
        const params = new URLSearchParams();
        const names = [
            'fullName', 'dateOfBirthFrom', 'dateOfBirthTo', 'gender',
            'placeOfBirth', 'hometown', 'ethnicity', 'religion', 'workplace', 'identityNumber'
        ];
        names.forEach((name) => {
            const value = field(name);
            if (value) params.set(name, value);
        });
        params.set('page', page);
        params.set('pageSize', el('resListPageSize').value);
        return params.toString();
    };

    const formatDate = (iso) => {
        if (!iso) return '—';
        const date = new Date(iso);
        if (Number.isNaN(date.getTime())) return iso.slice(0, 10);
        return new Intl.DateTimeFormat('vi-VN', {
            timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', year: 'numeric'
        }).format(date);
    };

    const textOrDash = (value) => (value == null || value === '' ? '—' : value);

    const renderRow = (item) => {
        const tr = document.createElement('tr');
        const values = [
            textOrDash(item.fullName),
            formatDate(item.dateOfBirth),
            textOrDash(item.gender),
            textOrDash(item.placeOfBirth),
            textOrDash(item.hometown),
            textOrDash(item.ethnicity),
            textOrDash(item.religion),
            textOrDash(item.workplace),
            textOrDash(item.identityNumber)
        ];
        values.forEach((value, index) => {
            const td = document.createElement('td');
            if (index === 0) td.className = 'mono';
            td.textContent = value;
            tr.appendChild(td);
        });
        return tr;
    };

    const render = (result) => {
        el('resListLoading').hidden = true;

        const total = result.total;
        const page = result.page;
        const totalPages = Math.max(1, Math.ceil(total / result.pageSize));

        el('resListBody').replaceChildren(...result.items.map(renderRow));
        el('resListEmpty').hidden = total > 0;
        el('resListTable').hidden = total === 0;
        el('resListPager').hidden = total === 0;
        el('resListPageInfo').textContent = `Trang ${page} / ${totalPages} (${total} bản ghi)`;

        const prev = el('resListPrev');
        const next = el('resListNext');
        prev.disabled = page <= 1;
        prev.dataset.page = String(page - 1);
        next.disabled = page >= totalPages;
        next.dataset.page = String(page + 1);
    };

    const load = async (page = 1) => {
        try {
            const result = await api(`/api/nhan-khau?${buildQuery(page)}`);
            if (result.status === 401) return;
            if (!result.ok) {
                el('resListLoading').hidden = true;
                showAlert(result.data?.message ?? 'Không tải được danh sách nhân khẩu. Vui lòng thử lại.');
                return;
            }
            el('resListAlert').hidden = true;
            render(result.data);
        } catch {
            el('resListLoading').hidden = true;
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.');
        }
    };

    const onClick = (event) => {
        if (!inRoot(event.target)) return;

        const pagerButton = event.target.closest?.('#resListPrev, #resListNext');
        if (pagerButton && !pagerButton.disabled) {
            void load(Number(pagerButton.dataset.page));
            return;
        }
        if (event.target.closest?.('#resListReset')) {
            filterForm().reset();
            el('resListPageSize').value = '10';
            void load(1);
        }
    };

    const onSubmit = (event) => {
        if (event.target.id !== 'resListFilter') return;
        event.preventDefault();
        void load(1);
    };

    const onChange = (event) => {
        if (!inRoot(event.target) || event.target.id !== 'resListPageSize') return;
        void load(1);
    };

    const init = () => {
        const root = el(rootId);
        if (!root) return;
        if (root !== mountedRoot) mountedRoot = root;
        if (!bound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('change', onChange);
            bound = true;
        }
        void load(1);
    };

    // Dự phòng: enhancedload không bắt được trường hợp Blazor thay DOM lúc SSR -> interactive.
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
