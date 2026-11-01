// Business Bank (demo): the customer-facing web app.
// Log in with a customer ID (C-01 to C-07) and the password 123.
// Only C-01 has notifications in this demo; the other customers see an empty list.
// Nothing is saved: reloading the page logs you out and resets everything.

/* ------------------------------------------------------------------ */
/* Mock data                                                           */
/* ------------------------------------------------------------------ */

const DEMO_PASSWORD = '123';

// The test customers from the build guide
const CUSTOMERS = {
  'C-01': 'Non Baraka LLC',
  'C-02': 'Sariq Non LLC',
  'C-03': 'Samarqand Non LLC',
  'C-04': "Digital Yo'l LLC",
  'C-05': 'Yangi Non LLC',
  'C-06': 'Oziq-Ovqat Savdo LLC',
  'C-07': 'Kichik Non'
};

// Notifications as the notification writer agent publishes them (publish_notification).
// offer is only present when the loan advisor made one.
const NOTIFICATIONS = {
  'C-01': [
    {
      id: 'N-101',
      lotId: 'T-1001',
      minutesAgo: 25,
      title: 'Bread supply for schools of Yunusobod district',
      body: "Yunusobod district's school education department is buying fresh bread for 12 schools for the school year. "
        + 'Bread must arrive every day before 8:00, and the bakery needs a sanitary certificate. Bids close on 30 November 2026.',
      buyer: 'Yunusobod district school education department',
      region: 'Tashkent',
      startPrice: 240000000,
      deadline: '2026-11-30',
      paymentTermsDays: 30,
      requirements: [
        'Daily delivery to 12 schools before 8:00',
        'Sanitary certificate for the bakery',
        'At least 1 year in business'
      ],
      whyText: "You already deliver bread to Kindergarten No 7, you're based in Yunusobod, and this contract fits the size of your business.",
      offer: { financing: 120000000, guarantee: 7200000, preApprovedLimit: 150000000 }
    },
    {
      id: 'N-102',
      lotId: 'T-1002',
      minutesAgo: 140,
      title: 'Bread for kindergarten No. 12',
      body: 'Kindergarten No. 12 is buying fresh bread for its children. Deliveries are daily, '
        + 'and the bakery needs a sanitary certificate. Bids close on 20 November 2026.',
      buyer: 'Kindergarten No. 12',
      region: 'Tashkent',
      startPrice: 60000000,
      deadline: '2026-11-20',
      paymentTermsDays: 30,
      requirements: [
        'Daily delivery before 8:00',
        'Sanitary certificate for the bakery'
      ],
      whyText: 'You already deliver bread to a kindergarten, and this small contract is easy for a bakery of your size.',
      offer: { financing: 30000000, guarantee: 1800000, preApprovedLimit: 150000000 }
    }
  ]
};

const LOAN_PRODUCTS = {
  financing: { name: 'Contract financing', limitKey: 'financing', hasTerm: true },
  guarantee: { name: 'Bid guarantee', limitKey: 'guarantee', hasTerm: false }
};

/* ------------------------------------------------------------------ */
/* State and helpers                                                   */
/* ------------------------------------------------------------------ */

const state = {
  customerId: null,  // set after login
  read: new Set()    // notification ids the customer has seen
};

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// 120000000 -> "120 000 000"
const money = (n) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

const formatDate = (iso) =>
  new Date(iso + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

const timeAgo = (minutes) => {
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  return `${hours} ${hours === 1 ? 'hour' : 'hours'} ago`;
};

const myNotifications = () => NOTIFICATIONS[state.customerId] || [];
const unreadCount = () => myNotifications().filter((n) => !state.read.has(n.id)).length;

const ICON_CHECK = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" class="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7"/></svg>`;
const ICON_SPARK = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" class="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true"><path d="M12 3.5l1.8 4.7 4.7 1.8-4.7 1.8L12 16.5l-1.8-4.7L5.5 10l4.7-1.8z"/></svg>`;
const ICON_DOC = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" class="size-4" aria-hidden="true"><path d="M6 3h8.5L19 7.5V21H6z"/><path d="M14 3v5h5M9 12.5h7M9 16.5h7"/></svg>`;
const ICON_BACK = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-4" aria-hidden="true"><path d="M15 6l-6 6 6 6"/></svg>`;

const BTN_PRIMARY =
  'inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm ' +
  'transition-colors hover:bg-brand-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600';
const INPUT =
  'h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm text-ink outline-none transition ' +
  'focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15';

/* ------------------------------------------------------------------ */
/* Login                                                               */
/* ------------------------------------------------------------------ */

function handleLogin(e) {
  e.preventDefault();
  const id = $('#login-id').value.trim().toUpperCase();
  const password = $('#login-password').value;
  const error = $('#login-error');

  if (!CUSTOMERS[id] || password !== DEMO_PASSWORD) {
    error.textContent = 'That customer ID or password is wrong. Check both and try again.';
    error.classList.remove('hidden');
    return;
  }

  error.classList.add('hidden');
  state.customerId = id;
  state.read = new Set();
  e.target.reset();
  goTo('#home');
}

function logout() {
  state.customerId = null;
  state.read = new Set();
  goTo('#login');
}

// Changing the hash triggers route() through hashchange; if it's already there, route directly
function goTo(hash) {
  if (location.hash === hash) route();
  else location.hash = hash;
}

/* ------------------------------------------------------------------ */
/* Screens                                                             */
/* ------------------------------------------------------------------ */

function updateBell(onNotificationsPage) {
  const count = unreadCount();
  const badge = $('#bell-count');
  badge.hidden = count === 0;
  badge.textContent = count;
  $('#bell').setAttribute('aria-label', count ? `Notifications, ${count} new` : 'Notifications');
  $('#bell').classList.toggle('bg-white/15', Boolean(onNotificationsPage));
}

function ringBell() {
  const icon = $('#bell-icon');
  icon.classList.remove('bell-ring');
  void icon.getBoundingClientRect(); // restart the animation
  icon.classList.add('bell-ring');
}

function renderHome() {
  $('#home-name').textContent = CUSTOMERS[state.customerId];
  if (unreadCount() > 0) ringBell();
}

function offerHtml(n) {
  const o = n.offer;
  return `
    <div class="border-t border-brand-100 bg-brand-50 px-5 py-4">
      <div class="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p class="text-xs font-medium text-brand-700">Pre-approved loan for this tender</p>
          <p class="mt-1 text-sm font-semibold">Contract financing up to ${money(o.financing)} so'm</p>
          <p class="text-sm text-muted">Bid guarantee of ${money(o.guarantee)} so'm to submit your bid</p>
          <p class="mt-1 text-xs text-muted">Within your pre-approved limit of ${money(o.preApprovedLimit)} so'm.</p>
        </div>
        <a href="#loan/${esc(n.lotId)}" class="${BTN_PRIMARY}">Request loan</a>
      </div>
    </div>`;
}

function notificationHtml(n) {
  const isNew = !state.read.has(n.id);
  const fact = (label, value) => `
    <div>
      <dt class="text-xs text-muted">${esc(label)}</dt>
      <dd class="mt-0.5 font-medium">${esc(value)}</dd>
    </div>`;

  return `
    <article class="overflow-hidden rounded-2xl border bg-surface shadow-sm shadow-cobalt/5 ${isNew ? 'border-brand-300' : 'border-line'}">
      <div class="p-5">
        <div class="flex items-center justify-between gap-3 text-xs">
          <span class="inline-flex items-center gap-1.5 font-medium text-brand-700">${ICON_DOC} New tender for you</span>
          <span class="flex items-center gap-2 text-muted">
            ${isNew ? '<span class="rounded-full bg-brand-600 px-2 py-0.5 font-medium text-white">New</span>' : ''}
            ${esc(timeAgo(n.minutesAgo))}
          </span>
        </div>

        <h2 class="mt-2 text-lg font-semibold">${esc(n.title)}</h2>
        <p class="mt-1.5 text-sm leading-relaxed text-ink/80">${esc(n.body)}</p>

        <dl class="mt-4 grid grid-cols-2 gap-3 rounded-xl bg-subtle px-4 py-3 text-sm sm:grid-cols-4">
          ${fact('Start price', `${money(n.startPrice)} so'm`)}
          ${fact('Bids close', formatDate(n.deadline))}
          ${fact('Payment', `${n.paymentTermsDays} days after delivery`)}
          ${fact('Region', n.region)}
        </dl>

        <h3 class="mt-4 text-xs font-medium text-muted">What the buyer needs</h3>
        <ul class="mt-1.5 space-y-1">
          ${n.requirements.map((r) => `<li class="flex gap-2 text-sm">${ICON_CHECK}<span>${esc(r)}</span></li>`).join('')}
        </ul>

        <p class="mt-4 flex gap-2 text-sm text-muted">${ICON_SPARK}<span><span class="font-medium text-ink">Why you got this:</span> ${esc(n.whyText)}</span></p>
      </div>
      ${n.offer ? offerHtml(n) : ''}
    </article>`;
}

function renderNotifications() {
  const list = myNotifications();
  $('#notification-list').innerHTML = list.length
    ? list.map(notificationHtml).join('')
    : `
      <div class="rounded-2xl border border-dashed border-line bg-surface px-6 py-14 text-center">
        <p class="font-medium">No notifications yet</p>
        <p class="mt-1 text-sm text-muted">When we find a state tender that fits your business, it will appear here.</p>
      </div>`;

  // Seeing the list counts as reading it: the "New" labels stay until the next visit
  list.forEach((n) => state.read.add(n.id));
}

function renderLoan(lotId) {
  const n = myNotifications().find((x) => x.lotId === lotId);
  if (!n || !n.offer) {
    location.hash = '#notifications';
    return;
  }

  $('#loan-view').innerHTML = `
    <a href="#notifications" class="inline-flex items-center gap-1.5 text-sm font-medium text-muted transition-colors hover:text-ink">${ICON_BACK} Notifications</a>
    <h1 class="mt-3 text-2xl font-semibold tracking-tight">Request a loan</h1>
    <p class="mt-1 text-sm text-muted">For the tender ${esc(n.lotId)}: ${esc(n.title)}</p>

    <form id="loan-form" novalidate class="mt-5 overflow-hidden rounded-2xl border border-line bg-surface shadow-sm shadow-cobalt/5">
      <div class="space-y-5 p-5">
        <div>
          <span class="mb-1.5 block text-sm font-medium">Loan type</span>
          <div role="group" aria-label="Loan type" class="grid grid-cols-2 gap-1 rounded-xl bg-subtle p-1 text-sm">
            ${Object.entries(LOAN_PRODUCTS).map(([key, p]) => `
              <button type="button" data-product="${key}" aria-pressed="${key === 'financing'}"
                class="rounded-lg px-3 py-2 font-medium text-muted transition-colors hover:text-ink aria-pressed:bg-surface aria-pressed:text-ink aria-pressed:shadow-sm">
                ${esc(p.name)}
              </button>`).join('')}
          </div>
        </div>

        <div>
          <label for="loan-amount" class="mb-1.5 block text-sm font-medium">Amount</label>
          <div class="relative">
            <input id="loan-amount" type="text" inputmode="numeric" autocomplete="off" class="${INPUT} pr-14 tabular-nums">
            <span class="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted">so'm</span>
          </div>
          <p id="loan-limit" class="mt-1.5 text-xs text-muted"></p>
          <p id="loan-error" role="alert" class="mt-1.5 hidden text-xs font-medium text-red-600"></p>
        </div>

        <div id="term-field">
          <label for="loan-term" class="mb-1.5 block text-sm font-medium">Term</label>
          <select id="loan-term" class="${INPUT} pr-8">
            <option value="3">3 months</option>
            <option value="6" selected>6 months</option>
            <option value="9">9 months</option>
            <option value="12">12 months</option>
          </select>
        </div>
      </div>

      <div class="flex flex-wrap items-center justify-between gap-3 border-t border-line bg-subtle px-5 py-4">
        <p class="text-xs text-muted">A loan manager reviews every request and contacts you.</p>
        <button type="submit" class="${BTN_PRIMARY}">Send request</button>
      </div>
    </form>`;

  let product = 'financing';
  const amount = $('#loan-amount');

  // Fills in the limit for the chosen loan type
  const selectProduct = (key) => {
    product = key;
    const p = LOAN_PRODUCTS[key];
    const limit = n.offer[p.limitKey];
    $$('[data-product]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.product === key)));
    amount.value = money(limit);
    $('#loan-limit').textContent = `Up to ${money(limit)} so'm, pre-approved for this tender.`;
    $('#loan-error').classList.add('hidden');
    $('#term-field').hidden = !p.hasTerm;
  };
  selectProduct('financing');

  $$('[data-product]').forEach((b) => b.addEventListener('click', () => selectProduct(b.dataset.product)));

  // Keep the amount readable: digits only, grouped with spaces
  amount.addEventListener('input', () => {
    const digits = amount.value.replace(/\D/g, '');
    amount.value = digits ? money(Number(digits)) : '';
  });

  $('#loan-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const value = Number(amount.value.replace(/\D/g, ''));
    const limit = n.offer[LOAN_PRODUCTS[product].limitKey];
    const error = $('#loan-error');

    if (!value || value > limit) {
      error.textContent = value ? `The amount can't be more than ${money(limit)} so'm.` : 'Enter an amount.';
      error.classList.remove('hidden');
      amount.focus();
      return;
    }

    const request = {
      customerId: state.customerId,
      lotId: n.lotId,
      product,
      amount: value,
      termMonths: LOAN_PRODUCTS[product].hasTerm ? Number($('#loan-term').value) : null,
      requestedAt: new Date().toISOString()
    };
    console.log('Loan request:', request);
    console.log(JSON.stringify(request, null, 2));

    renderLoanSent(n, request);
  });
}

function renderLoanSent(n, request) {
  const number = `LR-${String(Date.now()).slice(-5)}`;
  $('#loan-view').innerHTML = `
    <div class="mx-auto max-w-md rounded-2xl border border-line bg-surface px-6 py-10 text-center shadow-sm shadow-cobalt/5">
      <span class="mx-auto grid size-12 place-items-center rounded-full bg-brand-50 text-brand-600">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" class="size-6" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7"/></svg>
      </span>
      <h1 class="mt-4 text-xl font-semibold">Request sent</h1>
      <p class="mt-2 text-sm text-muted">
        We've received your request for ${money(request.amount)} so'm of ${esc(LOAN_PRODUCTS[request.product].name.toLowerCase())}
        for the tender ${esc(n.lotId)}. A loan manager will contact you soon.
      </p>
      <p class="mt-4 text-xs text-muted">Request number <span class="font-medium text-ink">${number}</span></p>
      <a href="#notifications" class="${BTN_PRIMARY} mt-6">Back to notifications</a>
    </div>`;
}

/* ------------------------------------------------------------------ */
/* Routing: #login, #home, #notifications, #loan/<lotId>               */
/* ------------------------------------------------------------------ */

function show(view) {
  const signedIn = view !== 'login';
  $('[data-view="login"]').hidden = signedIn;
  $('[data-shell]').hidden = !signedIn;
  $$('[data-shell] [data-view]').forEach((s) => { s.hidden = s.dataset.view !== view; });
  window.scrollTo(0, 0);
}

function route() {
  const [view, param] = location.hash.slice(1).split('/');

  if (!state.customerId) {
    if (view !== 'login') history.replaceState(null, '', '#login');
    show('login');
    $('#login-id').focus();
    return;
  }

  if (view === 'notifications') {
    show('notifications');
    renderNotifications(); // marks everything as read, so the badge clears
    updateBell(true);
  } else if (view === 'loan') {
    show('loan');
    renderLoan(param);
    updateBell(false);
  } else {
    if (view !== 'home') history.replaceState(null, '', '#home');
    show('home');
    updateBell(false);
    renderHome();
  }
}

/* ------------------------------------------------------------------ */
/* Start                                                               */
/* ------------------------------------------------------------------ */

$('#login-form').addEventListener('submit', handleLogin);
$('#logout').addEventListener('click', logout);
window.addEventListener('hashchange', route);
route();
