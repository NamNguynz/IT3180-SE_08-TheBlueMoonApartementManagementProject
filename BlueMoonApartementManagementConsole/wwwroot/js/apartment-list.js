(() => {
    // Trang Danh sách căn hộ: lọc/phân trang phía server, giữ nguyên mẫu JS chung của dự án:
    // listener gắn trên document, mountedRoot để nhận lần mount mới, dự phòng bằng MutationObserver.
    const rootId = 'apartmentList';
    let bound = false;
    let mountedRoot = null;
    let rootObserver = null;
    let buildingsLoadedFor = null;

    const el = (id) => document.getElementById(id);
    const inRoot = (node) => Boolean(node && document.getElementById(rootId)?.contains(node));
    const filterForm = () => el('apListFilter');
    const field = (name) => filterForm()?.elements.namedItem(name)?.value?.trim() ?? '';

    const showAlert = (message) => {
        const box = el('apListAlert');
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

    // Nhãn và class badge lấy từ ô chọn Trạng thái do Razor sinh ra (ApartmentStatusLabels).
    const statusMeta = () => Object.fromEntries(
        [...filterForm().elements.namedItem('status').options]
            .filter((o) => o.value)
            .map((o) => [o.value, { label: o.textContent.trim(), badge: o.dataset.badge || 'badge' }]));

    const buildQuery = (page) => {
        const params = new URLSearchParams();
        const code = field('code');
        const buildingId = field('buildingId');
        const areaFrom = field('areaFrom');
        const areaTo = field('areaTo');
        const floor = field('floor');
        const status = field('status');

        if (code) params.set('code', code);
        if (buildingId) params.set('buildingId', buildingId);
        if (areaFrom) params.set('areaFrom', areaFrom);
        if (areaTo) params.set('areaTo', areaTo);
        if (floor) params.set('floor', floor);
        if (status) params.set('status', status);

        params.set('page', page);
        params.set('pageSize', el('apListPageSize').value);
        return params.toString();
    };

    const formatArea = (value) =>
        Number(value).toLocaleString('vi-VN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const renderRow = (meta) => (item) => {
        const tr = document.createElement('tr');

        const codeCell = document.createElement('td');
        codeCell.className = 'mono';
        codeCell.textContent = item.code;
        tr.appendChild(codeCell);

        const buildingCell = document.createElement('td');
        buildingCell.textContent = item.buildingName;
        tr.appendChild(buildingCell);

        const countCell = document.createElement('td');
        countCell.textContent = String(item.residentCount);
        tr.appendChild(countCell);

        const areaCell = document.createElement('td');
        areaCell.textContent = formatArea(item.area);
        tr.appendChild(areaCell);

        const floorCell = document.createElement('td');
        floorCell.textContent = String(item.floor);
        tr.appendChild(floorCell);

        const statusCell = document.createElement('td');
        const info = meta[item.status] ?? { label: item.status, badge: 'badge' };
        const badge = document.createElement('span');
        badge.className = info.badge;
        badge.textContent = info.label;
        statusCell.appendChild(badge);
        tr.appendChild(statusCell);

        return tr;
    };

    const render = (result) => {
        el('apListLoading').hidden = true;

        const total = result.total;
        const page = result.page;
        const totalPages = Math.max(1, Math.ceil(total / result.pageSize));

        el('apListBody').replaceChildren(...result.items.map(renderRow(statusMeta())));
        el('apListEmpty').hidden = total > 0;
        el('apListTable').hidden = total === 0;
        el('apListPager').hidden = total === 0;
        el('apListPageInfo').textContent = `Trang ${page} / ${totalPages} (${total} bản ghi)`;

        const prev = el('apListPrev');
        const next = el('apListNext');
        prev.disabled = page <= 1;
        prev.dataset.page = String(page - 1);
        next.disabled = page >= totalPages;
        next.dataset.page = String(page + 1);
    };

    const load = async (page = 1) => {
        try {
            const result = await api(`/api/can-ho/danh-sach?${buildQuery(page)}`);
            if (result.status === 401) return;
            if (!result.ok) {
                el('apListLoading').hidden = true;
                showAlert(result.data?.message ?? 'Không tải được danh sách căn hộ. Vui lòng thử lại.');
                return;
            }
            el('apListAlert').hidden = true;
            render(result.data);
        } catch {
            el('apListLoading').hidden = true;
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.');
        }
    };

    const loadBuildings = async () => {
        if (buildingsLoadedFor === mountedRoot || !filterForm()) return;
        const result = await api('/api/toa-nha');
        if (!result.ok || !Array.isArray(result.data)) return;

        const select = filterForm().elements.namedItem('buildingId');
        const selected = select.value;
        const options = [new Option('Tất cả tòa nhà', '')];
        result.data.forEach((b) => options.push(new Option(b.name, String(b.id))));
        select.replaceChildren(...options);
        select.value = selected;
        buildingsLoadedFor = mountedRoot;
    };

    const onClick = (event) => {
        if (!inRoot(event.target)) return;

        const pagerButton = event.target.closest?.('#apListPrev, #apListNext');
        if (pagerButton && !pagerButton.disabled) {
            void load(Number(pagerButton.dataset.page));
            return;
        }
        if (event.target.closest?.('#apListReset')) {
            filterForm().reset();
            el('apListPageSize').value = '10';
            void load(1);
        }
    };

    const onSubmit = (event) => {
        if (event.target.id !== 'apListFilter') return;
        event.preventDefault();
        void load(1);
    };

    const onChange = (event) => {
        if (!inRoot(event.target) || event.target.id !== 'apListPageSize') return;
        void load(1);
    };

    const init = () => {
        const root = el(rootId);
        if (!root) return;
        if (root !== mountedRoot) {
            mountedRoot = root;
            buildingsLoadedFor = null;
        }
        if (!bound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('change', onChange);
            bound = true;
        }
        void loadBuildings();
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
