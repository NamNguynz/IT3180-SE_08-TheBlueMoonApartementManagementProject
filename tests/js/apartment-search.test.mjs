// Tự động hóa phần chạy trên trình duyệt của docs/testing/acceptance_apartment_search.md:
// K1 – K4 (lọc danh sách căn hộ theo mã, tòa, loại hình, diện tích) và K9 bước 4 (tối đa 5 kết quả).
// Chạy file JS thật của trang Hồ sơ căn hộ (wwwroot/js/apartment-profiles.js) trên DOM giả, API giả (xem dom-stub.mjs).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FakeElement, fakeForm, fakeSelect, jsonResponse, loadPage } from './dom-stub.mjs';

const STATUSES = [['DangChoThue', 'Đang cho thuê'], ['DaChoThue', 'Đã cho thuê'], ['DangSuaChua', 'Đang sửa chữa']];
const TYPES = [['', 'Tất cả loại hình'], ['CanHo', 'Căn hộ ở'], ['Kiot', 'Kiot'], ['Penthouse', 'Penthouse']];
const BUILDINGS = [{ id: 1, name: 'Tòa A' }, { id: 2, name: 'Tòa B' }];

/** Căn hộ giả theo dạng GET /api/can-ho trả về. */
const apartment = (id, code, buildingName, floor, area, type, status = 'DangChoThue') => ({
    id, code, buildingId: buildingName === 'Tòa A' ? 1 : 2, buildingName, floor, area, bedroomCount: 2,
    type, status, notes: null, household: null
});

/** Đúng 3 căn ở mục 3 "Dữ liệu test" của tài liệu acceptance. */
const K_APARTMENTS = [
    apartment(1, 'AT-K-01', 'Tòa A', 5, 70, 'CanHo'),
    apartment(2, 'AT-K-02', 'Tòa A', 1, 30, 'Kiot'),
    apartment(3, 'AT-K-03', 'Tòa B', 30, 250, 'Penthouse', 'DaChoThue')
];

/** Trang Hồ sơ căn hộ giả với đúng các id, ô lọc, bảng trạng thái của ApartmentProfilePage.razor. */
async function openApartmentPage(apartments = K_APARTMENTS) {
    const search = new FakeElement('input', { id: 'apSearch' });
    const area = new FakeElement('input', { id: 'apAreaSearch' });
    const typeFilter = fakeSelect({ id: 'apTypeFilter' }, TYPES);
    const list = new FakeElement('div', { id: 'apartmentList' });
    const count = new FakeElement('span', { id: 'apCount' });
    const empty = new FakeElement('p', { id: 'apListEmpty', hidden: true });
    const hint = new FakeElement('p', { id: 'apListHint', hidden: true });

    const board = new FakeElement('section', { id: 'apStatusBoard' });
    for (const [status] of STATUSES) {
        const column = new FakeElement('div', { className: 'ap-board-column', dataset: { status } });
        column.append(new FakeElement('span', { className: 'ap-board-count' }),
            new FakeElement('div', { className: 'ap-board-dropzone', dataset: { status } }));
        board.appendChild(column);
    }

    const elements = [
        search, area, typeFilter, list, count, empty, hint, board,
        fakeSelect({ id: 'apApartmentStatusLabels' }, STATUSES),
        fakeForm('apartmentForm', { buildingId: fakeSelect({ name: 'buildingId' }) })
    ];

    const page = await loadPage('apartment-profiles.js', {
        rootId: 'apartmentProfiles',
        elements,
        respond: (url) =>
            url === '/api/can-ho' ? jsonResponse(200, apartments)
                : url === '/api/toa-nha' ? jsonResponse(200, BUILDINGS)
                    : jsonResponse(404, null)
    });

    return {
        ...page,
        /** Gõ vào ô tìm hoặc ô diện tích (sự kiện input như khi người dùng gõ). */
        typeIn: (input, value) => { input.value = value; return page.dispatch('input', input); },
        /** Chọn ở ô Loại hình (sự kiện change). */
        chooseType: (value) => { typeFilter.value = value; return page.dispatch('change', typeFilter); },
        search, area,
        listed: () => list.children.map((button) => button.children[0].textContent),
        subtitles: () => list.children.map((button) => button.children[1].textContent),
        count: () => count.textContent,
        emptyShown: () => !empty.hidden,
        hintShown: () => !hint.hidden,
        boardCodes: () => board.querySelectorAll('.ap-board-dropzone')
            .flatMap((zone) => zone.children.map((card) => card.children[0].textContent))
    };
}

test('Mở trang: hiện đủ 3 căn, dòng đếm "3 căn hộ", mỗi căn có dòng mô tả tòa · tầng · loại hình (K2 bước 1)', async () => {
    const page = await openApartmentPage();

    assert.deepEqual(page.listed(), ['AT-K-01', 'AT-K-02', 'AT-K-03']);
    assert.equal(page.count(), '3 căn hộ');
    assert.deepEqual(page.subtitles(), ['Tòa A · Tầng 5 · Căn hộ ở', 'Tòa A · Tầng 1 · Kiot', 'Tòa B · Tầng 30 · Penthouse']);
    assert.equal(page.emptyShown(), false);
    assert.equal(page.hintShown(), false);
    assert.deepEqual(page.boardCodes().sort(), ['AT-K-01', 'AT-K-02', 'AT-K-03']);
});

test('K1: tra theo một phần mã căn (không phân biệt hoa thường), xóa ô tìm thì hiện lại đủ', async () => {
    const page = await openApartmentPage();

    await page.typeIn(page.search, 'K-02');
    assert.deepEqual(page.listed(), ['AT-K-02']);
    assert.equal(page.count(), '1 / 3 căn hộ');
    assert.deepEqual(page.boardCodes(), ['AT-K-02'], 'Bảng trạng thái lọc theo cùng điều kiện');

    await page.typeIn(page.search, 'k-02');
    assert.deepEqual(page.listed(), ['AT-K-02']);

    await page.typeIn(page.search, '');
    assert.deepEqual(page.listed(), ['AT-K-01', 'AT-K-02', 'AT-K-03']);
    assert.equal(page.count(), '3 căn hộ');
});

test('K2: lọc theo loại hình Kiot, Penthouse, Căn hộ ở; "Tất cả loại hình" thì hiện lại đủ', async () => {
    const page = await openApartmentPage();

    await page.chooseType('Kiot');
    assert.deepEqual(page.listed(), ['AT-K-02']);
    assert.equal(page.count(), '1 / 3 căn hộ');

    await page.chooseType('Penthouse');
    assert.deepEqual(page.listed(), ['AT-K-03']);

    await page.chooseType('CanHo');
    assert.deepEqual(page.listed(), ['AT-K-01']);

    await page.chooseType('');
    assert.deepEqual(page.listed(), ['AT-K-01', 'AT-K-02', 'AT-K-03']);
    assert.equal(page.count(), '3 căn hộ');
});

test('K2 bước 4: sau khi AT-K-01 đổi sang Penthouse, lọc Penthouse ra 2 căn', async () => {
    const changed = K_APARTMENTS.map((a) => (a.code === 'AT-K-01' ? { ...a, type: 'Penthouse' } : a));
    const page = await openApartmentPage(changed);

    await page.chooseType('Penthouse');

    assert.deepEqual(page.listed(), ['AT-K-01', 'AT-K-03']);
    assert.equal(page.count(), '2 / 3 căn hộ');
});

test('K3: tra theo diện tích chính xác; không có căn khớp thì hiện "Chưa có căn hộ phù hợp"', async () => {
    const page = await openApartmentPage();

    await page.typeIn(page.area, '250');
    assert.deepEqual(page.listed(), ['AT-K-03']);
    assert.equal(page.emptyShown(), false);

    await page.typeIn(page.area, '68.5');
    assert.deepEqual(page.listed(), []);
    assert.equal(page.count(), '0 / 3 căn hộ');
    assert.equal(page.emptyShown(), true);
    assert.deepEqual(page.boardCodes(), []);
});

test('K4: kết hợp ô tìm theo tên tòa, loại hình và diện tích; xóa hết thì hiện lại đủ', async () => {
    const page = await openApartmentPage();

    await page.typeIn(page.search, 'Tòa A');
    await page.chooseType('Kiot');
    assert.deepEqual(page.listed(), ['AT-K-02']);

    await page.typeIn(page.area, '70');
    assert.deepEqual(page.listed(), [], 'Không căn nào thỏa cả ba điều kiện');
    assert.equal(page.emptyShown(), true);

    await page.typeIn(page.search, '');
    await page.typeIn(page.area, '');
    await page.chooseType('');
    assert.deepEqual(page.listed(), ['AT-K-01', 'AT-K-02', 'AT-K-03']);
    assert.equal(page.count(), '3 căn hộ');
});

test('K9 bước 4: hơn 5 căn khớp thì danh sách chỉ hiện 5, có dòng nhắc; bảng trạng thái vẫn hiện đủ', async () => {
    const many = [
        ...Array.from({ length: 6 }, (_, i) => apartment(10 + i, `AT-L-0${i + 1}`, 'Tòa A', i + 1, 60, 'CanHo')),
        apartment(20, 'AT-X-01', 'Tòa B', 2, 60, 'CanHo')
    ];
    const page = await openApartmentPage(many);

    await page.typeIn(page.search, 'AT-L');

    assert.equal(page.listed().length, 5);
    assert.equal(page.count(), 'Hiển thị 5 / 6 căn hộ phù hợp');
    assert.equal(page.hintShown(), true);
    assert.equal(page.boardCodes().length, 6);
});
