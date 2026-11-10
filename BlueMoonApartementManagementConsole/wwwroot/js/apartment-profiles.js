(() => {
    const rootId = 'apartmentProfiles';
    let apartments = [];
    let selectedApartment = null;
    let residents = [];
    let householdId = null;
    let initializedRoot;

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

    const renderApartments = () => {
        const query = $('#apSearch').value.trim().toLocaleLowerCase('vi');
        const areaValue = $('#apAreaSearch').value.trim();
        const areaInCents = areaValue === '' ? null : Math.round(Number(areaValue) * 100);
        const type = $('#apTypeFilter').value;
        const labels = typeLabels();
        const matching = apartments.filter((apartment) =>
            (!query ||
                apartment.code.toLocaleLowerCase('vi').includes(query) ||
                apartment.building.toLocaleLowerCase('vi').includes(query)) &&
            (areaInCents === null || Math.round(Number(apartment.area) * 100) === areaInCents) &&
            (!type || apartment.type === type));

        $('#apCount').textContent = query || areaInCents !== null || type
            ? `${matching.length} / ${apartments.length} căn hộ`
            : `${apartments.length} căn hộ`;
        $('#apListLoading').hidden = true;
        $('#apListEmpty').hidden = matching.length > 0;

        const list = $('#apartmentList');
        list.replaceChildren(...matching.map((apartment) => {
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
            $('#apSelectedBadge').textContent = selectedApartment.status;
            $('#apSelectedBadge').className = `badge ${selectedApartment.status.toLocaleLowerCase('vi').includes('đang') ? 'badge-ok' : 'badge-muted'}`;
            fillApartmentForm(selectedApartment);
            fillHousehold(selectedApartment.household);
            $('#apAlert').hidden = true;
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
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
    };

    const fillHousehold = (household) => {
        const form = $('#householdForm');
        householdId = household?.id ?? null;
        residents = (household?.residents ?? []).map((resident) => ({ ...resident }));
        $('#householdTitle').textContent = household ? `Hộ khẩu ${household.householdNumber}` : 'Tạo hồ sơ hộ gia đình';
        $('#householdBadge').textContent = household ? `${residents.length} nhân khẩu` : 'Chưa lập hộ khẩu';
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
                resident.identityNumber ? `CCCD ${resident.identityNumber}` : ''
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
            ? `${residents.length} nhân khẩu`
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
        if (!button || !document.getElementById(rootId).contains(button)) return;

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
        if (existingIndex >= 0) residents[existingIndex] = resident;
        else residents.push(resident);

        renderResidents();
        $('#residentDialog').hidden = true;
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root || root === initializedRoot) return;
        initializedRoot = root;
        root.addEventListener('click', onClick);
        root.addEventListener('submit', (event) => {
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
        });
        $('#apSearch').addEventListener('input', renderApartments);
        $('#apAreaSearch').addEventListener('input', renderApartments);
        $('#apTypeFilter').addEventListener('change', renderApartments);
        void loadApartments(false);
    };

    init();
    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
