(() => {
    const rootId = 'apartmentProfiles';
    let apartments = [];
    let selectedApartment = null;
    let residents = [];
    let householdId = null;
    // Listener gắn trên document để không mất khi Blazor thay DOM; mountedRoot dùng để nhận ra lần mount mới.
    let bound = false;
    let mountedRoot = null;
    let rootObserver = null;
    let draggingApartmentId = null;

    const inRoot = (node) => Boolean(node && document.getElementById(rootId)?.contains(node));

    // Danh sách tìm kiếm chỉ hiển thị tối đa 5 căn; bảng trạng thái vẫn lấy toàn bộ kết quả.
    const APARTMENT_LIST_LIMIT = 5;
    // Căn hộ đang có nhân khẩu thì không được chuyển sang hai trạng thái này.
    const RESIDENTS_BLOCKED_STATUSES = ['DangChoThue', 'DangSuaChua'];

    const $ = (selector) => document.querySelector(`#${rootId} ${selector}`);
    const formValue = (form, name) => form.elements.namedItem(name).value;
    const setFormValue = (form, name, value) => {
        form.elements.namedItem(name).value = value ?? '';
    };

    const showAlert = (message, success = false) => {
        const alert = $('#apAlert');
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
            // Successful responses such as 204 do not have a JSON body.
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

    const dateInputValue = (value) => value ? value.slice(0, 10) : '';
    const dateTimeValue = (value) => value ? `${value}T00:00:00+07:00` : null;

    // Nhãn loại hình lấy từ các option của ô lọc (trang sinh ra từ ApartmentTypeLabels.cs)
    const typeLabels = () => Object.fromEntries(
        [...$('#apTypeFilter').options]
            .filter((o) => o.value)
            .map((o) => [o.value, o.textContent.trim()]));

    // PBI-007: nhãn tình trạng cư trú lấy từ datalist (trang sinh ra từ ResidentChangeLabels.cs).
    // Nhân khẩu mới thêm ở form này chưa có tình trạng, coi như đang cư trú.
    const statusLabels = () => Object.fromEntries(
        [...$('#apResidenceStatusLabels').options].map((o) => [o.value, o.textContent.trim()]));
    // Nhãn/class badge của trạng thái căn hộ lấy từ datalist do Razor sinh ra
    const apartmentStatusLabels = () => Object.fromEntries(
        [...$('#apApartmentStatusLabels').options].map((o) => [o.value, o.textContent.trim()]));
    const apartmentStatusBadge = (status) => status === 'DaChoThue'
        ? 'badge badge-ok'
        : status === 'DangSuaChua' ? 'badge badge-warn' : 'badge badge-muted';
    const currentResidentCount = () =>
        residents.filter((resident) => resident.residenceStatus !== 'DaChuyenDi').length;
    // Nhân khẩu Đã chuyển đi không tính; dữ liệu lấy từ household.residents trong response của API.
    const hasActiveResidents = (apartment) => Boolean(apartment?.household?.residents?.some(
        (resident) => resident.residenceStatus !== 'DaChuyenDi'));
    const isStatusBlocked = (apartment, status) =>
        RESIDENTS_BLOCKED_STATUSES.includes(status) && hasActiveResidents(apartment);
    const residentsBlockedMessage = () =>
        'Không thể chuyển căn hộ đang có nhân khẩu sang Đang cho thuê hoặc Đang sửa chữa. ' +
        'Vui lòng ghi nhận biến động chuyển đi cho nhân khẩu trước.';

    const filteredApartments = () => {
        const query = $('#apSearch').value.trim().toLocaleLowerCase('vi');
        const areaValue = $('#apAreaSearch').value.trim();
        const areaInCents = areaValue === '' ? null : Math.round(Number(areaValue) * 100);
        const type = $('#apTypeFilter').value;
        return {
            query, areaInCents, type,
            matching: apartments.filter((apartment) =>
                (!query ||
                    apartment.code.toLocaleLowerCase('vi').includes(query) ||
                    apartment.building.toLocaleLowerCase('vi').includes(query)) &&
                (areaInCents === null || Math.round(Number(apartment.area) * 100) === areaInCents) &&
                (!type || apartment.type === type))
        };
    };

    const renderBoard = () => {
        const board = $('#apStatusBoard');
        const labels = apartmentStatusLabels();
        const { matching } = filteredApartments();
        for (const column of board.querySelectorAll('.ap-board-column')) {
            const status = column.dataset.status;
            const items = matching.filter((apartment) => apartment.status === status);
            const zone = column.querySelector('.ap-board-dropzone');
            zone.replaceChildren(...items.map((apartment) => {
                const locked = hasActiveResidents(apartment);
                const card = document.createElement('div');
                card.className = 'ap-board-card';
                card.classList.toggle('is-active', selectedApartment?.id === apartment.id);
                card.classList.toggle('is-locked', locked);
                card.draggable = !locked;
                card.tabIndex = 0;
                card.setAttribute('role', 'button');
                card.setAttribute('aria-label',
                    `${apartment.code} – ${labels[apartment.status] ?? apartment.status}${locked ? ' (đang có nhân khẩu)' : ''}`);
                if (locked) card.title = residentsBlockedMessage();
                card.dataset.action = 'select-apartment';
                card.dataset.id = apartment.id;
                const code = document.createElement('strong');
                code.textContent = apartment.code;
                const meta = document.createElement('span');
                meta.textContent = `${apartment.building} · Tầng ${apartment.floor}`;
                card.append(code, meta);
                return card;
            }));
            column.querySelector('.ap-board-count').textContent = items.length;
        }
    };

    const renderApartments = () => {
        const { query, areaInCents, type, matching } = filteredApartments();
        const labels = typeLabels();
        const shown = matching.slice(0, APARTMENT_LIST_LIMIT);

        $('#apCount').textContent = matching.length > APARTMENT_LIST_LIMIT
            ? `Hiển thị ${APARTMENT_LIST_LIMIT} / ${matching.length} căn hộ phù hợp`
            : query || areaInCents !== null || type
                ? `${matching.length} / ${apartments.length} căn hộ`
                : `${apartments.length} căn hộ`;
        $('#apListLoading').hidden = true;
        $('#apListEmpty').hidden = matching.length > 0;
        $('#apListHint').hidden = matching.length <= APARTMENT_LIST_LIMIT;

        const list = $('#apartmentList');
        list.replaceChildren(...shown.map((apartment) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'apartment-list-item';
            button.classList.toggle('is-active', selectedApartment?.id === apartment.id);
            button.dataset.action = 'select-apartment';
            button.dataset.id = apartment.id;

            const title = document.createElement('span');
            title.className = 'apartment-list-code';
            title.textContent = apartment.code;
            const subtitle = document.createElement('span');
            subtitle.className = 'apartment-list-meta';
            subtitle.textContent = `${apartment.building} · Tầng ${apartment.floor} · ${labels[apartment.type] ?? apartment.type}`;
            const badge = document.createElement('span');
            badge.className = apartment.household ? 'badge badge-ok' : 'badge badge-muted';
            badge.textContent = apartment.household ? 'Đã có hộ khẩu' : 'Chưa có hộ khẩu';
            button.append(title, subtitle, badge);
            return button;
        }));
        renderBoard();
    };

    const loadApartments = async (keepSelection = true) => {
        $('#apListLoading').hidden = false;
        $('#apListEmpty').hidden = true;
        try {
            const response = await api('/api/can-ho');
            if (response.status === 401) return;
            if (!response.ok) {
                $('#apListLoading').hidden = true;
                showAlert(messageFor(response, 'Không tải được danh sách căn hộ.'));
                return;
            }

            apartments = response.data ?? [];
            renderApartments();
            if (keepSelection && selectedApartment) {
                const refreshed = apartments.find((item) => item.id === selectedApartment.id);
                if (refreshed) await selectApartment(refreshed.id);
                else startNewApartment();
            }
        } catch {
            $('#apListLoading').hidden = true;
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    const selectApartment = async (id) => {
        try {
            const response = await api(`/api/can-ho/${id}`);
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được hồ sơ căn hộ.'));
                return;
            }

            selectedApartment = response.data;
            renderApartments();
            $('#apSelectionEmpty').hidden = true;
            $('#apSelection').hidden = false;
            $('#householdPanel').hidden = false;
            $('#apSelectedTitle').textContent = selectedApartment.code;
            const statusLabel = apartmentStatusLabels()[selectedApartment.status] ?? selectedApartment.status;
            $('#apSelectedBadge').textContent = statusLabel;
            $('#apSelectedBadge').className = apartmentStatusBadge(selectedApartment.status);
            fillApartmentForm(selectedApartment);
            fillHousehold(selectedApartment.household);
            $('#apAlert').hidden = true;
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    // Khi có nhân khẩu chưa chuyển đi, chỉ giữ option Đã cho thuê và option đang dùng (để form gửi đúng giá trị cũ).
    const syncStatusOptions = (apartment) => {
        const select = $('#apartmentForm').elements.namedItem('status');
        const locked = hasActiveResidents(apartment);
        for (const option of select.options) {
            option.disabled = locked && option.value !== apartment?.status;
        }
        $('#apStatusHint').hidden = !locked;
    };

    const fillApartmentForm = (apartment) => {
        const form = $('#apartmentForm');
        setFormValue(form, 'code', apartment.code);
        setFormValue(form, 'building', apartment.building);
        setFormValue(form, 'floor', apartment.floor);
        setFormValue(form, 'area', apartment.area);
        setFormValue(form, 'bedroomCount', apartment.bedroomCount);
        setFormValue(form, 'status', apartment.status);
        setFormValue(form, 'type', apartment.type);
        setFormValue(form, 'notes', apartment.notes);
        form.querySelector('[data-role="save-apartment"]').textContent = 'Lưu thay đổi';
        syncStatusOptions(apartment);
    };

    const fillHousehold = (household) => {
        const form = $('#householdForm');
        householdId = household?.id ?? null;
        residents = (household?.residents ?? []).map((resident) => ({ ...resident }));
        $('#householdTitle').textContent = household ? `Hộ khẩu ${household.householdNumber}` : 'Tạo hồ sơ hộ gia đình';
        $('#householdBadge').textContent = household ? `${currentResidentCount()} nhân khẩu` : 'Chưa lập hộ khẩu';
        $('#householdBadge').className = household ? 'badge badge-ok' : 'badge badge-muted';
        $('#householdHint').hidden = Boolean(household);

        setFormValue(form, 'householdNumber', household?.householdNumber);
        setFormValue(form, 'headName', household?.headName);
        setFormValue(form, 'houseNumber', household?.houseNumber ??
            selectedApartment?.code ?? formValue($('#apartmentForm'), 'code'));
        setFormValue(form, 'registeredAt', dateInputValue(household?.registeredAt));
        form.querySelector('[data-role="save-household"]').textContent =
            household ? 'Lưu hồ sơ hộ khẩu' : 'Tạo hồ sơ hộ khẩu';
        renderResidents();
    };

    const renderResidents = () => {
        const list = $('#residentList');
        const residenceLabels = statusLabels();
        list.replaceChildren(...residents.map((resident, index) => {
            const card = document.createElement('article');
            card.className = 'resident-card';

            const info = document.createElement('div');
            info.className = 'resident-card-info';
            const name = document.createElement('strong');
            name.textContent = resident.fullName;
            const details = document.createElement('span');
            const detailParts = [
                resident.gender,
                resident.dateOfBirth ? `Sinh ${dateInputValue(resident.dateOfBirth)}` : '',
                resident.identityNumber ? `CCCD ${resident.identityNumber}` : '',
                // Chỉ ghi khi khác thường trú, để thẻ của đa số nhân khẩu không dài thêm
                resident.residenceStatus && resident.residenceStatus !== 'ThuongTru'
                    ? residenceLabels[resident.residenceStatus] ?? resident.residenceStatus
                    : ''
            ].filter(Boolean);
            details.textContent = detailParts.join(' · ');
            info.append(name, details);

            const edit = document.createElement('button');
            edit.type = 'button';
            edit.className = 'btn-link';
            edit.dataset.action = 'edit-resident';
            edit.dataset.index = index;
            edit.textContent = 'Chỉnh sửa';
            card.append(info, edit);
            return card;
        }));
        $('#residentEmpty').hidden = residents.length > 0;
        $('#householdBadge').textContent = householdId
            ? `${currentResidentCount()} nhân khẩu`
            : 'Chưa lập hộ khẩu';
    };

    const startNewApartment = () => {
        selectedApartment = null;
        residents = [];
        householdId = null;
        $('#apAlert').hidden = true;
        $('#apSelectionEmpty').hidden = true;
        $('#apSelection').hidden = false;
        $('#apSelectedTitle').textContent = 'Căn hộ mới';
        $('#apSelectedBadge').textContent = 'Chưa lưu';
        $('#apSelectedBadge').className = 'badge';

        const form = $('#apartmentForm');
        form.reset();
        setFormValue(form, 'floor', '0');
        setFormValue(form, 'bedroomCount', '0');
        syncStatusOptions(null);
        form.querySelector('[data-role="save-apartment"]').textContent = 'Tạo căn hộ';
        fillHousehold(null);
        $('#householdPanel').hidden = true;
        renderApartments();
    };

    const cancelApartmentEdit = () => {
        if (selectedApartment) {
            selectApartment(selectedApartment.id);
            return;
        }
        $('#apSelection').hidden = true;
        $('#apSelectionEmpty').hidden = false;
        renderApartments();
    };

    const openResidentDialog = (resident = null) => {
        const form = $('#residentForm');
        form.reset();
        setFormValue(form, 'id', resident?.id);
        for (const field of [
            'fullName', 'alias', 'dateOfBirth', 'gender', 'placeOfBirth', 'hometown',
            'ethnicity', 'religion', 'occupation', 'workplace', 'identityNumber',
            'identityIssuedAt', 'identityIssuePlace', 'previousResidence', 'notes'
        ]) {
            const value = resident?.[field];
            setFormValue(form, field, field === 'dateOfBirth' || field === 'identityIssuedAt'
                ? dateInputValue(value)
                : value);
        }
        $('#residentDialogTitle').textContent = resident ? 'Cập nhật nhân khẩu' : 'Thêm nhân khẩu';
        $('#residentDialog').hidden = false;
        form.elements.namedItem('fullName').focus();
    };

    const readResident = (form) => {
        const id = formValue(form, 'id');
        const resident = {
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
            identityIssuePlace: formValue(form, 'identityIssuePlace').trim() || null,
            previousResidence: formValue(form, 'previousResidence').trim() || null,
            notes: formValue(form, 'notes').trim() || null
        };
        if (id) resident.id = Number(id);
        return resident;
    };

    const onClick = async (event) => {
        const button = event.target.closest('[data-action]');
        if (!button || !inRoot(button)) return;

        switch (button.dataset.action) {
            case 'new-apartment':
                startNewApartment();
                break;
            case 'refresh':
                button.disabled = true;
                await loadApartments();
                button.disabled = false;
                break;
            case 'select-apartment':
                await selectApartment(Number(button.dataset.id));
                break;
            case 'cancel-apartment':
                cancelApartmentEdit();
                break;
            case 'save-apartment':
                event.preventDefault();
                await onApartmentSubmit($('#apartmentForm'));
                break;
            case 'save-household':
                event.preventDefault();
                await onHouseholdSubmit($('#householdForm'));
                break;
            case 'add-resident':
                openResidentDialog();
                break;
            case 'edit-resident':
                openResidentDialog(residents[Number(button.dataset.index)]);
                break;
            case 'close-resident':
                $('#residentDialog').hidden = true;
                break;
        }
    };

    const changeApartmentStatus = async (id, status) => {
        const apartment = apartments.find((item) => item.id === id);
        if (isStatusBlocked(apartment, status)) {
            showAlert(residentsBlockedMessage());
            return;
        }
        const response = await api(`/api/can-ho/${id}/trang-thai`,
            { method: 'PATCH', body: { status } });
        if (response.status === 401) return;
        if (!response.ok) {
            showAlert(messageFor(response, 'Không đổi được trạng thái căn hộ.'));
            return;
        }
        const label = apartmentStatusLabels()[status] ?? status;
        await loadApartments(true);
        showAlert(`Đã chuyển căn hộ sang trạng thái "${label}".`, true);
    };

    const onDragStart = (event) => {
        const card = event.target.closest('.ap-board-card');
        if (!inRoot(card) || !event.dataTransfer) return;
        draggingApartmentId = Number(card.dataset.id);
        event.dataTransfer.setData('text/plain', card.dataset.id);
        event.dataTransfer.effectAllowed = 'move';
        card.classList.add('is-dragging');
    };

    const onDragOver = (event) => {
        const column = event.target.closest('.ap-board-column');
        if (!inRoot(column) || !event.dataTransfer) return;
        const blocked = isStatusBlocked(
            apartments.find((item) => item.id === draggingApartmentId), column.dataset.status);
        event.preventDefault();
        event.dataTransfer.dropEffect = blocked ? 'none' : 'move';
        column.classList.toggle('is-drop-blocked', blocked);
        column.classList.toggle('is-drag-over', !blocked);
    };

    const onDragLeave = (event) => {
        const column = event.target.closest('.ap-board-column');
        if (inRoot(column) && !column.contains(event.relatedTarget))
            column.classList.remove('is-drag-over', 'is-drop-blocked');
    };

    const onDrop = async (event) => {
        const column = event.target.closest('.ap-board-column');
        if (!inRoot(column) || !event.dataTransfer) return;
        event.preventDefault();
        column.classList.remove('is-drag-over', 'is-drop-blocked');
        const id = Number(event.dataTransfer.getData('text/plain') || draggingApartmentId);
        const status = column.dataset.status;
        const apartment = apartments.find((item) => item.id === id);
        if (!apartment || apartment.status === status) return;
        await changeApartmentStatus(id, status);
    };

    const onDragEnd = () => {
        const board = $('#apStatusBoard');
        if (board) board.querySelectorAll('.is-dragging, .is-drag-over, .is-drop-blocked')
            .forEach((element) => element.classList.remove('is-dragging', 'is-drag-over', 'is-drop-blocked'));
        draggingApartmentId = null;
    };

    const onApartmentSubmit = async (form) => {
        if (!form.reportValidity()) {
            showAlert('Vui lòng kiểm tra các trường bắt buộc và giá trị đã nhập.');
            return;
        }

        const body = {
            code: formValue(form, 'code').trim(),
            building: formValue(form, 'building').trim(),
            floor: Number(formValue(form, 'floor')),
            area: Number(formValue(form, 'area')),
            bedroomCount: Number(formValue(form, 'bedroomCount')),
            status: formValue(form, 'status').trim(),
            type: formValue(form, 'type'),
            notes: formValue(form, 'notes').trim() || null
        };
        const isUpdate = Boolean(selectedApartment);
        const button = form.querySelector('[type="submit"]');
        const originalLabel = button.textContent;
        button.disabled = true;
        button.textContent = 'Đang lưu...';

        try {
            const response = await api(
                isUpdate ? `/api/can-ho/${selectedApartment.id}` : '/api/can-ho',
                { method: isUpdate ? 'PUT' : 'POST', body });
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không lưu được hồ sơ căn hộ.'));
                return;
            }

            await loadApartments(false);
            await selectApartment(response.data.id);
            // Hiện sau selectApartment, vì selectApartment ẩn khung thông báo
            showAlert(isUpdate ? 'Đã cập nhật hồ sơ căn hộ.' : 'Đã tạo hồ sơ căn hộ.', true);
        } catch (error) {
            console.error('Không thể tạo/cập nhật hồ sơ căn hộ.', error);
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        } finally {
            button.disabled = false;
            // Chỉ trả tên cũ khi lưu lỗi; lưu thành công thì giữ tên nút do form vừa đặt ("Lưu thay đổi")
            if (button.textContent === 'Đang lưu...') button.textContent = originalLabel;
        }
    };

    const onHouseholdSubmit = async (form) => {
        if (!form.reportValidity()) {
            showAlert('Vui lòng kiểm tra các trường bắt buộc và giá trị đã nhập.');
            return;
        }
        if (!selectedApartment) {
            showAlert('Vui lòng lưu hồ sơ căn hộ trước khi lập hộ khẩu.');
            return;
        }

        const body = {
            householdNumber: formValue(form, 'householdNumber').trim(),
            headName: formValue(form, 'headName').trim(),
            houseNumber: formValue(form, 'houseNumber').trim(),
            registeredAt: dateTimeValue(formValue(form, 'registeredAt')),
            residents
        };
        const isUpdate = Boolean(householdId);
        const button = form.querySelector('[type="submit"]');
        const originalLabel = button.textContent;
        button.disabled = true;
        button.textContent = 'Đang lưu...';

        try {
            const response = await api(
                isUpdate ? `/api/ho-gia-dinh/${householdId}` :
                    `/api/ho-gia-dinh/can-ho/${selectedApartment.id}`,
                { method: isUpdate ? 'PUT' : 'POST', body });
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không lưu được hồ sơ hộ khẩu.'));
                return;
            }

            await loadApartments(false);
            await selectApartment(selectedApartment.id);
            // Hiện sau selectApartment, vì selectApartment ẩn khung thông báo
            showAlert(isUpdate ? 'Đã cập nhật hồ sơ hộ khẩu và nhân khẩu.' :
                'Đã tạo hồ sơ hộ khẩu và nhân khẩu.', true);
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        } finally {
            button.disabled = false;
            // Chỉ trả tên cũ khi lưu lỗi; lưu thành công thì giữ tên nút do form vừa đặt ("Lưu hồ sơ hộ khẩu")
            if (button.textContent === 'Đang lưu...') button.textContent = originalLabel;
        }
    };

    const onResidentSubmit = (event) => {
        event.preventDefault();
        // Listener gắn ở #apartmentProfiles nên currentTarget là <section>; form vừa submit là event.target
        const form = event.target;
        if (!form.reportValidity()) return;

        const resident = readResident(form);
        const existingIndex = resident.id
            ? residents.findIndex((item) => item.id === resident.id)
            : -1;
        // Form không có tình trạng cư trú (PBI-007), giữ lại tình trạng cũ để thẻ và số nhân khẩu không sai
        if (existingIndex >= 0) residents[existingIndex] = { ...resident, residenceStatus: residents[existingIndex].residenceStatus };
        else residents.push(resident);

        renderResidents();
        $('#residentDialog').hidden = true;
    };

    const onSubmit = (event) => {
        if (!inRoot(event.target)) return;
        // Không dùng event.target.id: residentForm có <input name="id"> nên form.id là ô input đó, không phải chuỗi id
        const formId = event.target.getAttribute('id');
        if (formId === 'apartmentForm') {
            event.preventDefault();
            void onApartmentSubmit(event.target);
        }
        if (formId === 'householdForm') {
            event.preventDefault();
            void onHouseholdSubmit(event.target);
        }
        if (formId === 'residentForm') onResidentSubmit(event);
    };

    const onFilterInput = (event) => {
        if (!inRoot(event.target)) return;
        if (['apSearch', 'apAreaSearch'].includes(event.target.getAttribute('id'))) renderApartments();
    };

    const onFilterChange = (event) => {
        if (event.target.getAttribute('id') === 'apTypeFilter' && inRoot(event.target)) renderApartments();
    };

    // Bàn phím: Enter/Space trên thẻ thì chọn căn hộ để đổi trạng thái qua ô select trong form
    const onBoardKeydown = (event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        const card = event.target.closest('.ap-board-card');
        if (!inRoot(card)) return;
        event.preventDefault();
        void selectApartment(Number(card.dataset.id));
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root) return;
        if (root !== mountedRoot) {
            mountedRoot = root;
            selectedApartment = null;
            residents = [];
            householdId = null;
            draggingApartmentId = null;
        }
        if (!bound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('input', onFilterInput);
            document.addEventListener('change', onFilterChange);
            document.addEventListener('dragstart', onDragStart);
            document.addEventListener('dragover', onDragOver);
            document.addEventListener('dragleave', onDragLeave);
            document.addEventListener('drop', (event) => { void onDrop(event); });
            document.addEventListener('dragend', onDragEnd);
            document.addEventListener('keydown', onBoardKeydown);
            bound = true;
        }
        void loadApartments(false);
    };

    // Dự phòng: enhancedload không bắt được trường hợp Blazor thay DOM lúc SSR -> interactive.
    const observeRootChanges = () => {
        if (rootObserver) return;
        rootObserver = new MutationObserver(() => {
            const current = document.getElementById(rootId);
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
