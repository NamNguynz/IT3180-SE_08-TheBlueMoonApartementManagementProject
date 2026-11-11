// Trang Biến động nhân khẩu (PBI-007). Quy tắc thật nằm ở ResidentChangeService;
// file này chỉ ẩn/hiện ô nhập, nút theo tình trạng và gọi API api/ho-gia-dinh/{id}/bien-dong.
(() => {
    const rootId = 'residentChanges';
    let households = [];
    let residency = null;
    let initializedRoot;

    // Cấu hình hiển thị theo loại biến động (nhãn ngày, ô nơi đi/đến, có ngày kết thúc, có khai báo nhân khẩu mới)
    const changeTypes = {
        ChuyenDen: { fromLabel: 'Ngày chuyển đến', placeLabel: 'Nơi ở trước khi chuyển đến', placeRequired: false, hasEnd: false, newResident: true },
        TamTru: { fromLabel: 'Tạm trú từ ngày', placeLabel: 'Nơi thường trú', placeRequired: false, hasEnd: true, newResident: true },
        TamVang: { fromLabel: 'Tạm vắng từ ngày', placeLabel: 'Nơi đến trong thời gian tạm vắng', placeRequired: true, hasEnd: true, newResident: false },
        TroVe: { fromLabel: 'Ngày trở về', placeLabel: null, placeRequired: false, hasEnd: false, newResident: false },
        ChuyenDi: { fromLabel: 'Ngày chuyển đi', placeLabel: 'Nơi chuyển đến', placeRequired: true, hasEnd: false, newResident: false }
    };

    // Nút hiện cho từng tình trạng, giống bảng chuyển trạng thái ở ResidentChangeService
    const actionsFor = (status) => ({
        ThuongTru: ['TamVang', 'ChuyenDi'],
        TamTru: ['ChuyenDi'],
        TamVang: ['TroVe', 'ChuyenDi'],
        DaChuyenDi: []
    })[status] ?? [];

    const statusBadgeClass = (status) => ({
        ThuongTru: 'badge badge-ok',
        TamTru: 'badge badge-warn',
        TamVang: 'badge badge-warn',
        DaChuyenDi: 'badge badge-muted'
    })[status] ?? 'badge';

    const $ = (selector) => document.querySelector(`#${rootId} ${selector}`);
    const formValue = (form, name) => form.elements.namedItem(name).value;
    const setFormValue = (form, name, value) => {
        form.elements.namedItem(name).value = value ?? '';
    };

    const showAlert = (message, success = false, selector = '#rcAlert') => {
        const alert = $(selector);
        alert.textContent = message;
        alert.classList.toggle('alert-success', success);
        alert.hidden = false;
        alert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    };

    const api = async (url, options = {}) => {
        const response = await fetch(url, {
            method: options.method ?? 'GET',
            credentials: 'same-origin',
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {})
            },
            body: options.body ? JSON.stringify(options.body) : undefined
        });

        if (response.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }

        let data = null;
        try {
            data = await response.json();
        } catch {
            // Phản hồi không có JSON (ví dụ 204)
        }
        return { ok: response.ok, status: response.status, data };
    };

    const messageFor = (response, fallback) => {
        const data = response.data;
        const validationErrors = data?.errors
            ? Object.entries(data.errors)
                .flatMap(([field, messages]) =>
                    (Array.isArray(messages) ? messages : [messages])
                        .map((message) => `${field}: ${message}`))
                .join(' ')
            : null;
        const message = data?.message ?? data?.detail ?? validationErrors ?? data?.title;
        if (message) return message;
        if (response.status >= 500)
            return `Máy chủ gặp lỗi khi xử lý yêu cầu (${response.status}). Vui lòng kiểm tra log Backend.`;
        if (response.status === 403)
            return 'Tài khoản hiện tại không có quyền BQT để thực hiện thao tác.';
        return `${fallback} (HTTP ${response.status}).`;
    };

    const dateTimeValue = (value) => value ? `${value}T00:00:00+07:00` : null;
    // Ngày thuần: yyyy-MM-dd (DateOnly) hoặc ISO lúc 00:00 (ngày sinh); hiển thị dd/MM/yyyy, không đổi múi giờ
    const formatDate = (value) => {
        if (!value) return '';
        const [year, month, day] = value.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    };
    // Thời điểm (recordedAt, lưu UTC): phải đổi sang giờ máy người dùng, cắt chuỗi như trên sẽ lệch ngày từ 0h đến 7h
    const formatTime = (value) => value
        ? new Date(value).toLocaleString('vi-VN', {
            day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'
        })
        : '';
    const today = () => {
        const now = new Date();
        const pad = (n) => String(n).padStart(2, '0');
        return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
    };

    // Nhãn lấy từ datalist (trang sinh ra từ ResidentChangeLabels.cs)
    const labels = (selector) => Object.fromEntries(
        [...$(selector).options].map((o) => [o.value, o.textContent.trim()]));
    const typeLabel = (type) => labels('#rcTypeLabels')[type] ?? type;
    const statusLabel = (status) => labels('#rcStatusLabels')[status] ?? status;

    const isCurrent = (resident) => resident.residenceStatus !== 'DaChuyenDi';

    // ---- Danh sách hộ ----

    const renderHouseholds = () => {
        const query = $('#rcSearch').value.trim().toLocaleLowerCase('vi');
        const matching = households.filter((household) =>
            !query ||
            household.apartmentCode.toLocaleLowerCase('vi').includes(query) ||
            household.building.toLocaleLowerCase('vi').includes(query) ||
            household.householdNumber.toLocaleLowerCase('vi').includes(query));

        $('#rcCount').textContent = query
            ? `${matching.length} / ${households.length} hộ`
            : `${households.length} hộ`;
        $('#rcListLoading').hidden = true;
        $('#rcListEmpty').hidden = matching.length > 0;

        $('#householdList').replaceChildren(...matching.map((household) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'apartment-list-item';
            button.classList.toggle('is-active', residency?.householdId === household.id);
            button.dataset.action = 'select-household';
            button.dataset.id = household.id;

            const title = document.createElement('span');
            title.className = 'apartment-list-code';
            title.textContent = `${household.apartmentCode} · ${household.householdNumber}`;
            const subtitle = document.createElement('span');
            subtitle.className = 'apartment-list-meta';
            subtitle.textContent = household.building;
            const badge = document.createElement('span');
            badge.className = 'badge badge-ok';
            badge.textContent = `${household.currentResidents} nhân khẩu`;
            button.append(title, subtitle, badge);
            return button;
        }));
    };

    const loadHouseholds = async (keepSelection = true) => {
        $('#rcListLoading').hidden = false;
        $('#rcListEmpty').hidden = true;
        try {
            // Dùng lại API danh sách căn hộ của trang Hồ sơ căn hộ, chỉ lấy căn đã có hộ khẩu
            const response = await api('/api/can-ho');
            if (response.status === 401) return;
            if (!response.ok) {
                $('#rcListLoading').hidden = true;
                showAlert(messageFor(response, 'Không tải được danh sách hộ gia đình.'));
                return;
            }

            households = (response.data ?? [])
                .filter((apartment) => apartment.household)
                .map((apartment) => ({
                    id: apartment.household.id,
                    householdNumber: apartment.household.householdNumber,
                    apartmentCode: apartment.code,
                    building: apartment.building,
                    currentResidents: apartment.household.residents.filter(isCurrent).length
                }));
            renderHouseholds();
            if (keepSelection && residency) await selectHousehold(residency.householdId);
        } catch {
            $('#rcListLoading').hidden = true;
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    // ---- Nhân khẩu và lịch sử của hộ đang chọn ----

    const selectHousehold = async (id) => {
        try {
            const response = await api(`/api/ho-gia-dinh/${id}/bien-dong`);
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được biến động của hộ.'));
                return;
            }

            residency = response.data;
            $('#rcAlert').hidden = true;
            renderResidency();
            renderHouseholds();
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    const renderResidency = () => {
        const household = households.find((item) => item.id === residency.householdId);
        $('#rcSelectionEmpty').hidden = true;
        $('#rcSelection').hidden = false;
        $('#rcHouseholdTitle').textContent = household
            ? `Hộ khẩu ${residency.householdNumber} · Căn ${household.apartmentCode}`
            : `Hộ khẩu ${residency.householdNumber}`;
        $('#rcHouseholdBadge').textContent = `${residency.residents.filter(isCurrent).length} nhân khẩu đang cư trú`;
        renderResidents();
        renderHistory();
    };

    const renderResidents = () => {
        $('#rcResidentList').replaceChildren(...residency.residents.map((resident) => {
            const card = document.createElement('article');
            card.className = 'resident-card';

            const info = document.createElement('div');
            info.className = 'resident-card-info';
            const name = document.createElement('strong');
            name.textContent = resident.fullName;
            const details = document.createElement('span');
            details.textContent = [
                resident.gender,
                resident.dateOfBirth ? `Sinh ${formatDate(resident.dateOfBirth)}` : '',
                resident.statusUntil ? `Đến ${formatDate(resident.statusUntil)}` : ''
            ].filter(Boolean).join(' · ');
            const status = document.createElement('span');
            status.className = statusBadgeClass(resident.residenceStatus);
            status.textContent = statusLabel(resident.residenceStatus);
            info.append(name, details, status);

            const actions = document.createElement('div');
            actions.className = 'resident-card-actions';
            for (const type of actionsFor(resident.residenceStatus)) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn-link';
                button.dataset.action = 'open-change';
                button.dataset.type = type;
                button.dataset.resident = resident.id;
                button.textContent = typeLabel(type);
                actions.append(button);
            }
            card.append(info, actions);
            return card;
        }));
        $('#rcResidentEmpty').hidden = residency.residents.length > 0;
    };

    const renderHistory = () => {
        $('#rcHistory').replaceChildren(...residency.changes.map((change) => {
            const row = document.createElement('tr');
            const period = change.toDate
                ? `${formatDate(change.fromDate)} – ${formatDate(change.toDate)}`
                : formatDate(change.fromDate);
            for (const text of [
                period,
                typeLabel(change.type),
                change.residentName,
                change.place ?? '',
                change.reason ?? '',
                `${change.recordedBy} · ${formatTime(change.recordedAt)}`
            ]) {
                const cell = document.createElement('td');
                cell.textContent = text;
                row.append(cell);
            }
            return row;
        }));
        $('#rcHistoryEmpty').hidden = residency.changes.length > 0;
    };

    // ---- Hộp thoại ghi nhận ----

    // Ẩn và tắt (disabled) ô không dùng, để form.reportValidity() bỏ qua ô bị ẩn
    const toggleFields = (container, visible, required = null) => {
        container.hidden = !visible;
        for (const field of container.querySelectorAll('input, select, textarea')) {
            field.disabled = !visible;
            if (required !== null) field.required = visible && required;
        }
    };

    const openChangeDialog = (type, residentId = null) => {
        const config = changeTypes[type];
        const form = $('#changeForm');
        form.reset();
        $('#rcDialogAlert').hidden = true;
        setFormValue(form, 'type', type);
        setFormValue(form, 'residentId', residentId);
        $('#changeDialogTitle').textContent = typeLabel(type);

        const resident = residency.residents.find((item) => item.id === residentId);
        $('#changeResidentName').hidden = !resident;
        $('#changeResidentName').textContent = resident
            ? `Nhân khẩu: ${resident.fullName} (${statusLabel(resident.residenceStatus)})`
            : '';

        toggleFields($('#rcNewResident'), config.newResident);

        form.querySelector('[data-role="from-label"]').textContent = config.fromLabel;
        const fromDate = form.elements.namedItem('fromDate');
        fromDate.max = today();
        // Không ghi lùi về trước lần biến động gần nhất của nhân khẩu (server cũng kiểm)
        fromDate.min = resident?.lastChangeDate ?? '';
        fromDate.value = today();

        toggleFields(form.querySelector('[data-role="to-field"]'), config.hasEnd, config.hasEnd);
        toggleFields(form.querySelector('[data-role="place-field"]'), Boolean(config.placeLabel), config.placeRequired);
        form.querySelector('[data-role="place-label"]').textContent = config.placeLabel ?? '';
        form.querySelector('[data-role="place-required"]').hidden = !config.placeRequired;

        $('#changeDialog').hidden = false;
        (config.newResident ? form.elements.namedItem('fullName') : fromDate).focus();
    };

    const readNewResident = (form) => ({
        fullName: formValue(form, 'fullName').trim(),
        alias: formValue(form, 'alias').trim() || null,
        dateOfBirth: dateTimeValue(formValue(form, 'dateOfBirth')),
        gender: formValue(form, 'gender'),
        placeOfBirth: formValue(form, 'placeOfBirth').trim() || null,
        hometown: formValue(form, 'hometown').trim() || null,
        ethnicity: formValue(form, 'ethnicity').trim() || null,
        religion: formValue(form, 'religion').trim() || null,
        occupation: formValue(form, 'occupation').trim() || null,
        workplace: formValue(form, 'workplace').trim() || null,
        identityNumber: formValue(form, 'identityNumber').trim() || null,
        identityIssuedAt: dateTimeValue(formValue(form, 'identityIssuedAt')),
        identityIssuePlace: formValue(form, 'identityIssuePlace').trim() || null
    });

    const onChangeSubmit = async (form) => {
        if (!form.reportValidity()) return;

        const type = formValue(form, 'type');
        const config = changeTypes[type];
        const residentId = formValue(form, 'residentId');
        const body = {
            type,
            residentId: residentId ? Number(residentId) : null,
            newResident: config.newResident ? readNewResident(form) : null,
            fromDate: formValue(form, 'fromDate'),
            toDate: config.hasEnd ? formValue(form, 'toDate') || null : null,
            place: config.placeLabel ? formValue(form, 'place').trim() || null : null,
            reason: formValue(form, 'reason').trim() || null
        };

        const button = form.querySelector('[type="submit"]');
        button.disabled = true;
        button.textContent = 'Đang lưu...';
        try {
            const response = await api(`/api/ho-gia-dinh/${residency.householdId}/bien-dong`, { method: 'POST', body });
            if (response.status === 401) return;
            if (!response.ok) {
                // Hiện lỗi ngay trong hộp thoại để người dùng sửa mà không mất dữ liệu đã nhập
                showAlert(messageFor(response, 'Không ghi nhận được biến động.'), false, '#rcDialogAlert');
                return;
            }

            $('#changeDialog').hidden = true;
            residency = response.data;
            await loadHouseholds(false);
            renderResidency();
            showAlert(`Đã ghi nhận: ${typeLabel(type)}.`, true);
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.', false, '#rcDialogAlert');
        } finally {
            button.disabled = false;
            button.textContent = 'Ghi nhận';
        }
    };

    const onClick = async (event) => {
        const button = event.target.closest('[data-action]');
        if (!button || !document.getElementById(rootId).contains(button)) return;

        switch (button.dataset.action) {
            case 'refresh':
                button.disabled = true;
                await loadHouseholds();
                button.disabled = false;
                break;
            case 'select-household':
                await selectHousehold(Number(button.dataset.id));
                break;
            case 'open-change':
                openChangeDialog(button.dataset.type,
                    button.dataset.resident ? Number(button.dataset.resident) : null);
                break;
            case 'close-change':
                $('#changeDialog').hidden = true;
                break;
        }
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root || root === initializedRoot) return;
        initializedRoot = root;
        households = [];
        residency = null;
        root.addEventListener('click', onClick);
        root.addEventListener('submit', (event) => {
            // Dùng getAttribute: form có <input name="type">, tránh nhầm thuộc tính của form với ô nhập
            if (event.target.getAttribute('id') !== 'changeForm') return;
            event.preventDefault();
            void onChangeSubmit(event.target);
        });
        $('#rcSearch').addEventListener('input', renderHouseholds);
        void loadHouseholds(false);
    };

    init();
    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
