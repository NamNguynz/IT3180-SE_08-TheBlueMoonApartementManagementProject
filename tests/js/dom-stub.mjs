// DOM giả tối thiểu để chạy file JS thật của trang (wwwroot/js) trong Node, không cần trình duyệt hay gói ngoài.
// Chạy: node --test tests/js/*.test.mjs
//
// Giới hạn: đây không phải trình duyệt. Test kiểm logic của file JS (gửi API gì, ghi chữ gì vào ô nào,
// nút nào bị tắt, chuyển trang đi đâu), không kiểm màu sắc, bố cục, CSS.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const JS_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)),
    '../../BlueMoonApartementManagementConsole/wwwroot/js');

/** Phần tử giả. Ghi bằng innerHTML/outerHTML/insertAdjacentHTML sẽ ném lỗi: trang phải ghi chữ bằng textContent. */
export class FakeElement {
    constructor(tag = 'div', props = {}) {
        this.tagName = tag.toUpperCase();
        this.id = '';
        this.className = '';
        this.textContent = '';
        this.value = '';
        this.hidden = false;
        this.disabled = false;
        this.dataset = {};
        this.children = [];
        this.parentElement = null;
        this.attributes = {};
        Object.assign(this, props);
    }

    appendChild(child) {
        child.parentElement = this;
        this.children.push(child);
        return child;
    }

    append(...children) {
        children.forEach((c) => this.appendChild(c));
    }

    replaceChildren(...children) {
        children.forEach((c) => { c.parentElement = this; });
        this.children = children;
    }

    /** classList đọc, ghi trên className, giống trình duyệt. */
    get classList() {
        const names = () => this.className.split(/\s+/).filter(Boolean);
        const save = (list) => { this.className = list.join(' '); };
        return {
            contains: (name) => names().includes(name),
            add: (...added) => save([...new Set([...names(), ...added])]),
            remove: (...removed) => save(names().filter((n) => !removed.includes(n))),
            toggle: (name, force) => {
                const has = names().includes(name);
                const want = force === undefined ? !has : Boolean(force);
                if (want && !has) save([...names(), name]);
                if (!want && has) save(names().filter((n) => n !== name));
                return want;
            }
        };
    }

    setAttribute(name, value) {
        if (name === 'id') this.id = String(value);
        else this.attributes[name] = String(value);
    }

    getAttribute(name) {
        return name === 'id' ? this.id : (this.attributes[name] ?? null);
    }

    /** Bộ chọn đơn giản: '#id', '.class' hoặc tên thẻ; nhiều bộ chọn cách nhau bằng dấu phẩy. */
    matches(selector) {
        return selector.split(',').map((s) => s.trim()).some((s) =>
            s.startsWith('#') ? this.id === s.slice(1)
                : s.startsWith('.') ? this.classList.contains(s.slice(1))
                    : this.tagName === s.toUpperCase());
    }

    closest(selector) {
        for (let node = this; node; node = node.parentElement) {
            if (node.matches?.(selector)) return node;
        }
        return null;
    }

    *descendants() {
        for (const child of this.children) {
            yield child;
            if (child.descendants) yield* child.descendants();
        }
    }

    querySelectorAll(selector) {
        return [...this.descendants()].filter((e) => e.matches?.(selector));
    }

    querySelector(selector) {
        return this.querySelectorAll(selector)[0] ?? null;
    }

    contains(node) {
        for (let n = node; n; n = n.parentElement) {
            if (n === this) return true;
        }
        return false;
    }

    focus() { }
    scrollIntoView() { }

    set innerHTML(html) { throw new Error(`Không được ghi innerHTML: ${html}`); }
    get innerHTML() { throw new Error('Không được đọc innerHTML'); }
    set outerHTML(html) { throw new Error(`Không được ghi outerHTML: ${html}`); }
    insertAdjacentHTML(_, html) { throw new Error(`Không được dùng insertAdjacentHTML: ${html}`); }
}

/** Form giả: elements.namedItem(tên) trả ô nhập, reset() đưa mọi ô về giá trị ban đầu. */
export function fakeForm(id, fields, { submitButton } = {}) {
    const defaults = Object.fromEntries(Object.entries(fields).map(([name, f]) => [name, f.value]));
    const form = new FakeElement('form', { id });
    form.resetCount = 0;
    form.elements = { namedItem: (name) => fields[name] ?? null };
    form.reset = () => {
        form.resetCount++;
        for (const [name, f] of Object.entries(fields)) f.value = defaults[name];
    };
    form.querySelector = (selector) => (selector === 'button[type="submit"]' ? submitButton ?? null : null);
    Object.values(fields).forEach((f) => { f.parentElement = form; });
    if (submitButton) submitButton.parentElement = form;
    return form;
}

/** Ô select giả: options luôn là các phần tử con, giống trình duyệt (kể cả khi trang replaceChildren). */
export function fakeSelect(props, options = []) {
    const select = new FakeElement('select', props);
    Object.defineProperty(select, 'options', { get: () => select.children });
    select.replaceChildren(...options.map(([value, text]) => new FakeElement('option', { value, textContent: text })));
    return select;
}

/** Phản hồi fetch giả. */
export const jsonResponse = (status, body) => ({
    status,
    ok: status >= 200 && status < 300,
    json: async () => body
});

/** Chờ các promise đang dở (fetch, res.json, render) chạy xong. */
export const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

/**
 * Nạp một file trong wwwroot/js vào trang giả.
 * elements: các phần tử có id trên trang. respond(url, init): trả phản hồi cho mỗi lần fetch.
 * rootId (tùy chọn): id của phần tử bao cả trang. Khi có rootId, phần tử chưa có cha được gắn vào root,
 * và id nào trang hỏi mà test không khai báo thì tạo tự động (test không kiểm các phần tử này).
 */
export async function loadPage(scriptName, { elements, respond, rootId }) {
    const byId = new Map(elements.map((e) => [e.id, e]));
    const listeners = {};
    const requests = [];
    const created = [];

    let root = null;
    if (rootId) {
        root = byId.get(rootId) ?? new FakeElement('section', { id: rootId });
        byId.set(rootId, root);
        for (const e of elements) {
            if (e !== root && !e.parentElement) root.appendChild(e);
        }
    }
    const findById = (id) => {
        if (byId.has(id)) return byId.get(id);
        const found = root?.querySelector(`#${id}`);
        if (found) return found;
        if (!root) return null;
        const auto = new FakeElement('div', { id });
        root.appendChild(auto);
        byId.set(id, auto);
        return auto;
    };

    const document = {
        documentElement: new FakeElement('html'),
        getElementById: (id) => (root ? findById(id) : byId.get(id) ?? null),
        // Chỉ hỗ trợ dạng '#root #id' hoặc '#id' như các file trong wwwroot/js đang dùng
        querySelector: (selector) => {
            const last = selector.trim().split(/\s+/).at(-1);
            return last.startsWith('#') ? findById(last.slice(1)) : root?.querySelector(last) ?? null;
        },
        createElement: (tag) => {
            const e = new FakeElement(tag);
            created.push(e);
            return e;
        },
        addEventListener: (type, fn) => { (listeners[type] ??= []).push(fn); }
    };
    // new Option(chữ, giá trị) như trình duyệt
    function Option(text, value) {
        return new FakeElement('option', { textContent: text, value });
    }
    class MutationObserver {
        observe() { }
        disconnect() { }
    }
    const location = {
        assigned: [],
        assign(url) { this.assigned.push(url); }
    };
    const fetch = async (url, init) => {
        requests.push({ url: String(url), init });
        return respond(String(url), init);
    };

    const context = vm.createContext({
        document, window: {}, location, fetch, URLSearchParams, setTimeout, console, Option, MutationObserver
    });
    const source = readFileSync(path.join(JS_DIR, scriptName), 'utf8');
    vm.runInContext(source, context, { filename: scriptName });
    await flush();

    /** Phát một sự kiện như trình duyệt (click, submit, change) rồi chờ trang xử lý xong. */
    const dispatch = async (type, target) => {
        const event = { type, target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
        for (const fn of listeners[type] ?? []) await fn(event);
        await flush();
        return event;
    };

    return { requests, created, location, dispatch, lastUrl: () => requests.at(-1)?.url };
}
