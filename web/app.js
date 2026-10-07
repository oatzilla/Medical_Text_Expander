
// ==========================================
// View Mode Toggle (Compact vs Expanded)
// ==========================================
let currentViewMode = localStorage.getItem('ward_view_mode') || 'compact';

function initViewMode() {
  const btnCompact = document.getElementById('btnViewCompact');
  const btnExpanded = document.getElementById('btnViewExpanded');
  const grid = document.getElementById('bedsGrid');

  function applyViewMode(mode) {
    currentViewMode = mode;
    try { localStorage.setItem('ward_view_mode', mode); } catch {}
    if (mode === 'expanded') {
      if (grid) grid.classList.add('view-expanded');
      if (btnExpanded) btnExpanded.classList.add('active');
      if (btnCompact) btnCompact.classList.remove('active');
    } else {
      if (grid) grid.classList.remove('view-expanded');
      if (btnCompact) btnCompact.classList.add('active');
      if (btnExpanded) btnExpanded.classList.remove('active');
    }
  }

  if (btnCompact) btnCompact.addEventListener('click', () => applyViewMode('compact'));
  if (btnExpanded) btnExpanded.addEventListener('click', () => applyViewMode('expanded'));

  applyViewMode(currentViewMode);
}
/**
 * Ward Bed Notes - Cloud Web & Mobile Portal Client
 * Real-time Supabase integration, Offline-first cache, Responsive UI
 */

const SUPABASE_URL = "https://jchjzorgnijhlywlereh.supabase.co";
const SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImpjaGp6b3JnbmlqaGx5d2xlcmVoIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTEzMzY1MTMsImV4cCI6MjEwNjkxMjUxM30.OdQmLt3hjbcCWKyjfgXZxZysSBXpIyVOGJeuYvAluSw";

// Normalize all line break variants (\r\n, \r, \n) into Windows standard CRLF (\r\n)
function normalizeToCRLF(text) {
  if (!text) return '';
  return text.replace(/\r\n/g, '\n').replace(/\r/g, '\n').replace(/\n/g, '\r\n');
}


// State
let bedsData = [];
let prevContentMap = new Map();
let currentFilter = 'all';
let currentSearch = '';
let activeBedNumber = 1;
let currentHistoryList = [];
let selectedHistoryItem = null;
let pollTimer = null;
let bedFetchBusy = false;
let bedGeneration = 0;
let bedVersion = null;
let bedFullFetchAt = 0;
let bedRetryAt = 0;
let bedFailures = 0;
document.addEventListener('visibilitychange', () => {
  if (!document.hidden && getCurrentUser()) fetchAllBeds();
});
let isSaving = false;

// ==========================================
// Multi-User & Workspace System (v1.8.0)
// ==========================================
let usersCatalogCache = [
  {
    id: "u_admin",
    username: "admin",
    display_name: "ผู้ดูแลระบบ (Admin)",
    password_hash: "9416a40b88fff19d0365e4c29fb2cd67fcd5022216708f1fa258b1513c56a41d",
    role: "admin",
    user_slot: 0,
    is_active: true,
    registered_via: "system",
    created_at: new Date().toISOString(),
    last_login_at: null
  }
];

function getCurrentUser() {
  try {
    let stored = sessionStorage.getItem('ward_current_user');
    if (!stored) {
      stored = localStorage.getItem('ward_current_user');
    }
    if (stored) {
      const parsed = JSON.parse(stored);
      if (parsed && parsed.username && parsed.is_active !== false) {
        return parsed;
      }
    }
  } catch {}
  return null;
}

function setCurrentUser(user, rememberMe = true) {
  const userData = {
    id: user.id || user.username,
    username: user.username,
    display_name: user.display_name,
    role: user.role,
    user_slot: user.user_slot || 0,
    is_active: user.is_active !== false,
    registered_via: user.registered_via || 'system'
  };
  try {
    if (rememberMe) {
      localStorage.setItem('ward_current_user', JSON.stringify(userData));
      sessionStorage.removeItem('ward_current_user');
    } else {
      sessionStorage.setItem('ward_current_user', JSON.stringify(userData));
      localStorage.removeItem('ward_current_user');
    }
  } catch {}
  resetIdleTimer();
  updateUserUI();
}

function clearCurrentUser() {
  try {
    localStorage.removeItem('ward_current_user');
    sessionStorage.removeItem('ward_current_user');
    localStorage.removeItem('ward_admin_active_workspace');
  } catch {}
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
  const banner = document.getElementById('idleTimeoutBanner');
  if (banner) banner.style.display = 'none';

  try {
    if (editModal && editModal.classList.contains('open')) {
      document.body.classList.remove('modal-open');
      editModal.classList.remove('open');
      editModal.setAttribute('aria-hidden', 'true');
    }
  } catch {}

  updateUserUI();
  initEmptyBeds();
  renderBeds();
  openLoginModal(true);
}

// ==========================================
// Idle Session Timeout System (v1.9.4)
// ==========================================
let lastWebActivityTime = Date.now();
let idleCheckInterval = null;

function getIdleTimeoutMinutes() {
  const val = localStorage.getItem('ward_idle_timeout_min');
  if (val === null || val === undefined) return 15;
  const parsed = parseInt(val, 10);
  return isNaN(parsed) ? 15 : parsed;
}

function setIdleTimeoutMinutes(min) {
  localStorage.setItem('ward_idle_timeout_min', String(min));
  resetIdleTimer();
}

function resetIdleTimer() {
  lastWebActivityTime = Date.now();
  try {
    sessionStorage.setItem('ward_last_activity', String(Date.now()));
  } catch {}
  const banner = document.getElementById('idleTimeoutBanner');
  if (banner && banner.style.display !== 'none') {
    banner.style.display = 'none';
  }
}

function initIdleTimeoutTracker() {
  try {
    const stored = sessionStorage.getItem('ward_last_activity');
    if (stored) {
      const t = parseInt(stored, 10);
      if (!isNaN(t) && t > 0) lastWebActivityTime = t;
    }
  } catch {}

  let throttleTimer = null;
  const onUserActivity = () => {
    if (!throttleTimer) {
      throttleTimer = setTimeout(() => {
        throttleTimer = null;
        resetIdleTimer();
      }, 500);
    }
  };

  ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll', 'click'].forEach(evt => {
    window.addEventListener(evt, onUserActivity, { passive: true });
  });

  const btnStay = document.getElementById('btnStayLoggedIn');
  if (btnStay) {
    btnStay.addEventListener('click', () => {
      resetIdleTimer();
      showToast('ขยายเวลาการใช้งานเรียบร้อยแล้ว', 'success');
    });
  }

  const selTimeout = document.getElementById('selectIdleTimeout');
  if (selTimeout) {
    selTimeout.value = String(getIdleTimeoutMinutes());
    selTimeout.addEventListener('change', (e) => {
      const min = parseInt(e.target.value, 10);
      setIdleTimeoutMinutes(min);
      showToast(`ตั้งเวลาพักหน้าจอเป็น ${min === 0 ? 'ปิดการใช้งาน' : min + ' นาที'} แล้ว`, 'info');
    });
  }

  if (idleCheckInterval) clearInterval(idleCheckInterval);
  idleCheckInterval = setInterval(checkIdleTimeout, 1000);
}

function checkIdleTimeout() {
  const user = getCurrentUser();
  if (!user) {
    const banner = document.getElementById('idleTimeoutBanner');
    if (banner && banner.style.display !== 'none') banner.style.display = 'none';
    return;
  }

  const timeoutMin = getIdleTimeoutMinutes();
  if (timeoutMin <= 0) {
    const banner = document.getElementById('idleTimeoutBanner');
    if (banner && banner.style.display !== 'none') banner.style.display = 'none';
    return;
  }

  try {
    const stored = sessionStorage.getItem('ward_last_activity');
    if (stored) {
      const t = parseInt(stored, 10);
      if (!isNaN(t) && t > lastWebActivityTime) {
        lastWebActivityTime = t;
      }
    }
  } catch {}

  const totalSec = timeoutMin * 60;
  const elapsedSec = Math.floor((Date.now() - lastWebActivityTime) / 1000);
  const remainingSec = totalSec - elapsedSec;

  const banner = document.getElementById('idleTimeoutBanner');
  const label = document.getElementById('idleTimeoutLabel');

  if (remainingSec <= 0) {
    if (banner) banner.style.display = 'none';
    handleWebSessionTimeout(timeoutMin);
  } else if (remainingSec <= 60) {
    if (label) {
      label.textContent = `⏳ ไม่มีการใช้งาน ระบบจะออกจากระบบอัตโนมัติในอีก ${remainingSec} วินาทีเพื่อความปลอดภัยของข้อมูลผู้ป่วย`;
    }
    if (banner && banner.style.display === 'none') {
      banner.style.display = 'flex';
    }
  } else {
    if (banner && banner.style.display !== 'none') {
      banner.style.display = 'none';
    }
  }
}

function handleWebSessionTimeout(timeoutMin) {
  try {
    if (editModal && editModal.classList.contains('open') && activeBedNumber > 0) {
      saveEditDraft(activeBedNumber);
      document.body.classList.remove('modal-open');
      editModal.classList.remove('open');
      editModal.setAttribute('aria-hidden', 'true');
    }
  } catch {}

  try {
    localStorage.removeItem('ward_current_user');
    sessionStorage.removeItem('ward_current_user');
    localStorage.removeItem('ward_admin_active_workspace');
  } catch {}

  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }

  initEmptyBeds();
  renderBeds();
  updateUserUI();

  const noticeMsg = `⚠️ ออกจากระบบอัตโนมัติเนื่องจากไม่มีการใช้งานเกิน ${timeoutMin} นาที เพื่อความปลอดภัยของข้อมูลผู้ป่วย กรุณาเข้าสู่ระบบใหม่`;
  openLoginModal(true, noticeMsg);
  showToast(noticeMsg, 'error');
}

function getActiveWorkspaceUser() {
  const curUser = getCurrentUser();
  if (!curUser) return 'admin';
  if (curUser.role !== 'admin') {
    return curUser.username;
  }
  const wsSelect = document.getElementById('workspaceSelect');
  if (wsSelect && wsSelect.value) {
    return wsSelect.value;
  }
  return localStorage.getItem('ward_admin_active_workspace') || 'admin';
}

function setActiveWorkspaceUser(username) {
  const curUser = getCurrentUser();
  if (!curUser || curUser.role !== 'admin') return;
  try {
    localStorage.setItem('ward_admin_active_workspace', username);
  } catch {}
  const wsSelect = document.getElementById('workspaceSelect');
  if (wsSelect && wsSelect.value !== username) {
    wsSelect.value = username;
  }
  prevContentMap.clear();
  updateWorkspaceNoticeBanner();
  initEmptyBeds();
  loadCachedBeds();
  fetchAllBeds();
}

function getActiveWorkspaceSlot() {
  const wsUser = getActiveWorkspaceUser();
  if (wsUser === 'admin') return 0;
  const user = usersCatalogCache.find(u => u.username.toLowerCase() === wsUser.toLowerCase());
  return user ? (user.user_slot || 0) : 0;
}

function getRemoteBedNumber(bedNum) {
  const slot = getActiveWorkspaceSlot();
  return slot === 0 ? bedNum : (slot * 100) + bedNum;
}

async function fetchUsersCatalogFromCloud() {
  try {
    const res = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.101&select=content,updated_at,updated_by`, {
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });
    if (res.ok) {
      const data = await res.json();
      if (data && data.length > 0 && data[0].content) {
        const parsed = JSON.parse(data[0].content);
        if (parsed && Array.isArray(parsed.users) && parsed.users.length > 0) {
          usersCatalogCache = parsed.users;
          try {
            localStorage.setItem('ward_users_catalog_cache', JSON.stringify(parsed));
          } catch {}
          updateWorkspaceSelectOptions();
          return usersCatalogCache;
        }
      }
    }
  } catch (err) {
    console.warn('fetchUsersCatalogFromCloud error:', err);
  }

  try {
    const cached = localStorage.getItem('ward_users_catalog_cache');
    if (cached) {
      const parsed = JSON.parse(cached);
      if (parsed && Array.isArray(parsed.users)) {
        usersCatalogCache = parsed.users;
      }
    }
  } catch {}
  updateWorkspaceSelectOptions();
  return usersCatalogCache;
}

async function saveUsersCatalogToCloud(usersList) {
  usersCatalogCache = usersList;
  const payload = {
    version: 1,
    users: usersList
  };
  const jsonStr = JSON.stringify(payload);
  try {
    localStorage.setItem('ward_users_catalog_cache', jsonStr);
  } catch {}

  try {
    const nowIso = new Date().toISOString();
    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.101`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: jsonStr,
        updated_at: nowIso,
        updated_by: getCurrentUser() ? (getCurrentUser().username || 'admin') : 'system'
      })
    });
  } catch (err) {
    console.error('saveUsersCatalogToCloud error:', err);
  }
}

async function ensureUserSlotRowsExist(userSlot, username) {
  if (!userSlot || userSlot <= 0) return true;
  try {
    const minBed = (userSlot * 100) + 1;
    const checkRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${minBed}&select=bed_number`, {
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });
    if (checkRes.ok) {
      const rows = await checkRes.json();
      if (rows && rows.length > 0) return true;
    }

    const rowsToInsert = [];
    const nowIso = new Date().toISOString();
    for (let i = 1; i <= 30; i++) {
      rowsToInsert.push({
        bed_number: (userSlot * 100) + i,
        content: '',
        updated_at: nowIso,
        updated_by: username || 'user'
      });
    }

    const postRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes`, {
      method: 'POST',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json',
        'Prefer': 'return=minimal'
      },
      body: JSON.stringify(rowsToInsert)
    });
    return postRes.ok;
  } catch (err) {
    console.warn('ensureUserSlotRowsExist error:', err);
    return false;
  }
}

function updateUserUI() {
  const curUser = getCurrentUser();
  const badgeName = document.getElementById('userBadgeName');
  const menuDisplayName = document.getElementById('menuUserDisplayName');
  const menuRole = document.getElementById('menuUserRole');
  const btnManageUsers = document.getElementById('btnOpenUserManagement');
  const wsWrapper = document.getElementById('workspaceSelectWrapper');

  if (!curUser) {
    if (badgeName) badgeName.textContent = 'ยังไม่เข้าสู่ระบบ';
    if (menuDisplayName) menuDisplayName.textContent = 'ยังไม่เข้าสู่ระบบ';
    if (menuRole) menuRole.textContent = 'กรุณาเข้าสู่ระบบ';
    if (btnManageUsers) btnManageUsers.style.display = 'none';
    if (wsWrapper) wsWrapper.style.display = 'none';
    const banner = document.getElementById('workspaceNoticeBanner');
    if (banner) banner.style.display = 'none';
    return;
  }

  if (badgeName) {
    badgeName.textContent = curUser.display_name || curUser.username;
  }

  if (menuDisplayName) {
    menuDisplayName.textContent = curUser.display_name || curUser.username;
  }

  if (menuRole) {
    menuRole.textContent = curUser.role === 'admin' ? '👑 ผู้ดูแลระบบ (Admin)' : '👩‍⚕️ พยาบาล / ผู้ใช้ทั่วไป';
  }

  if (curUser.role === 'admin') {
    if (btnManageUsers) btnManageUsers.style.display = 'flex';
    if (wsWrapper) wsWrapper.style.display = 'flex';
  } else {
    if (btnManageUsers) btnManageUsers.style.display = 'none';
    if (wsWrapper) wsWrapper.style.display = 'none';
  }

  updateWorkspaceSelectOptions();
  updateWorkspaceNoticeBanner();
}

function updateWorkspaceSelectOptions() {
  const wsSelect = document.getElementById('workspaceSelect');
  if (!wsSelect) return;
  const curUser = getCurrentUser();
  if (!curUser || curUser.role !== 'admin') return;

  const currentVal = wsSelect.value || localStorage.getItem('ward_admin_active_workspace') || 'admin';
  wsSelect.innerHTML = '';

  const optAdmin = document.createElement('option');
  optAdmin.value = 'admin';
  optAdmin.textContent = '👑 พื้นที่งาน: Admin (เตียงหลักวอร์ด)';
  wsSelect.appendChild(optAdmin);

  usersCatalogCache.forEach(u => {
    if (u.username.toLowerCase() !== 'admin' && u.is_active !== false) {
      const opt = document.createElement('option');
      opt.value = u.username;
      opt.textContent = `👩‍⚕️ เตียงของ: ${u.display_name} (${u.username})`;
      wsSelect.appendChild(opt);
    }
  });

  if (Array.from(wsSelect.options).some(o => o.value === currentVal)) {
    wsSelect.value = currentVal;
  } else {
    wsSelect.value = 'admin';
  }
}

function updateWorkspaceNoticeBanner() {
  const banner = document.getElementById('workspaceNoticeBanner');
  const label = document.getElementById('workspaceNoticeLabel');
  if (!banner) return;

  const curUser = getCurrentUser();
  const wsUser = getActiveWorkspaceUser();

  if (curUser && curUser.role === 'admin' && wsUser !== 'admin') {
    const targetUser = usersCatalogCache.find(u => u.username.toLowerCase() === wsUser.toLowerCase());
    const dName = targetUser ? `${targetUser.display_name} (${targetUser.username})` : wsUser;
    if (label) label.textContent = `กำลังดูและจัดการเตียงของ: ${dName} (โหมด Admin ควบคุม)`;
    banner.style.display = 'flex';
  } else {
    banner.style.display = 'none';
  }
}

function initMultiUserSession() {
  updateUserUI();
  fetchUsersCatalogFromCloud().then(() => {
    updateUserUI();
  });
}

// DOM Elements
const bedsGrid = document.getElementById('bedsGrid');
const cloudStatusBadge = document.getElementById('cloudStatusBadge');
const cloudStatusText = document.getElementById('cloudStatusText');
const themeToggleBtn = document.getElementById('themeToggleBtn');
const refreshBtn = document.getElementById('refreshBtn');
const searchInput = document.getElementById('searchInput');
const clearSearchBtn = document.getElementById('clearSearchBtn');
const tabButtons = document.querySelectorAll('.tab-btn');

// Stats Elements
const statTotalBeds = document.getElementById('statTotalBeds');
const statOccupiedBeds = document.getElementById('statOccupiedBeds');
const statAvailableBeds = document.getElementById('statAvailableBeds');
const statLastSyncTime = document.getElementById('statLastSyncTime');
const countAll = document.getElementById('countAll');
const countOccupied = document.getElementById('countOccupied');
const countAvailable = document.getElementById('countAvailable');

// Edit Modal Elements
const editModal = document.getElementById('editModal');
const modalBedBadge = document.getElementById('modalBedBadge');
const modalTitle = document.getElementById('modalTitle');
const noteTextarea = document.getElementById('noteTextarea');
const charCountDisplay = document.getElementById('charCountDisplay');
const authorInput = document.getElementById('authorInput');
const modalCloseBtn = document.getElementById('modalCloseBtn');
const btnCancelEdit = document.getElementById('btnCancelEdit');
const btnSaveBedNote = document.getElementById('btnSaveBedNote');
const btnClearBedNote = document.getElementById('btnClearBedNote');
const btnViewHistoryFromModal = document.getElementById('btnViewHistoryFromModal');
const quickTagBtns = document.querySelectorAll('.quick-tag-btn');

// History Modal Elements
const historyModal = document.getElementById('historyModal');
const historyBedBadge = document.getElementById('historyBedBadge');
const historyCloseBtn = document.getElementById('historyCloseBtn');
const btnCloseHistory = document.getElementById('btnCloseHistory');
const historyListPane = document.getElementById('historyListPane');
const historyPreviewText = document.getElementById('historyPreviewText');
const historySelectedMeta = document.getElementById('historySelectedMeta');
const btnCopyHistoryText = document.getElementById('btnCopyHistoryText');
const btnRestoreFromHistory = document.getElementById('btnRestoreFromHistory');

// Toast Container
const toastContainer = document.getElementById('toastContainer');

// ==========================================
// Initialization
// ==========================================
document.addEventListener('DOMContentLoaded', () => {
  initTheme();
  setupEventListeners();
  initIdleTimeoutTracker();
  initClinicalTemplates();
  initIoTemplateSystem();

  const curUser = getCurrentUser();
  if (!curUser) {
    initMultiUserSession();
    initEmptyBeds();
    renderBeds();
    openLoginModal(true);
  } else {
    initMultiUserSession();
    initAuthorName();
    initEmptyBeds();
    loadCachedBeds();
    fetchAllBeds();
    pollTimer = setInterval(fetchAllBeds, 15000);
  }
});

// Initialize 30 empty beds
function initEmptyBeds() {
  bedGeneration++;
  bedVersion = null;
  bedFullFetchAt = 0;
  bedRetryAt = 0;
  bedsData = [];
  for (let i = 1; i <= 30; i++) {
    bedsData.push({
      bed_number: i,
      content: '',
      updated_at: null,
      updated_by: ''
    });
  }
}

// Load cached beds from LocalStorage (Instant offline display)
function loadCachedBeds() {
  try {
    const wsUser = getActiveWorkspaceUser() || 'admin';
    const cached = localStorage.getItem('ward_bed_notes_cache_' + wsUser) || (wsUser === 'admin' ? localStorage.getItem('ward_bed_notes_cache') : null);
    if (cached) {
      const parsed = JSON.parse(cached);
      if (Array.isArray(parsed) && parsed.length > 0) {
        parsed.forEach(item => {
          const idx = bedsData.findIndex(b => b.bed_number === item.bed_number);
          if (idx !== -1) bedsData[idx] = item;
        });
        renderBeds();
        updateStats();
      }
    }
  } catch (e) {
    console.error('Error loading cached beds:', e);
  }
}

// ==========================================
// Theme Management
// ==========================================
function initTheme() {
  const savedTheme = localStorage.getItem('ward_theme') || (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
  document.documentElement.setAttribute('data-theme', savedTheme);
  updateThemeIcon(savedTheme);
}

function toggleTheme() {
  const current = document.documentElement.getAttribute('data-theme') || 'light';
  const next = current === 'dark' ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  localStorage.setItem('ward_theme', next);
  updateThemeIcon(next);
}

function updateThemeIcon(theme) {
  themeToggleBtn.innerHTML = theme === 'dark' ? '<i class="fa-solid fa-sun"></i>' : '<i class="fa-solid fa-moon"></i>';
}

// Author Name Management
function initAuthorName() {
  const curUser = getCurrentUser();
  const defaultAuthor = curUser ? (curUser.display_name || curUser.username) : '';
  const savedAuthor = localStorage.getItem('ward_author_name') || defaultAuthor;
  if (savedAuthor && authorInput) authorInput.value = savedAuthor;
}

function saveAuthorName(name) {
  if (name) localStorage.setItem('ward_author_name', name.trim());
}

// ==========================================
// Multi-User UI & Modal Management
// ==========================================
let isAuthModalMandatory = false;

function switchAuthTab(tabName) {
  const tabLogin = document.getElementById('tabBtnLogin');
  const tabReg = document.getElementById('tabBtnRegister');
  const formLogin = document.getElementById('loginForm');
  const formReg = document.getElementById('registerForm');
  const title = document.getElementById('authModalTitle');
  const errLogin = document.getElementById('loginErrorMessage');
  const errReg = document.getElementById('registerErrorMessage');

  if (errLogin) errLogin.style.display = 'none';
  if (errReg) errReg.style.display = 'none';

  if (tabName === 'register') {
    if (tabLogin) tabLogin.classList.remove('active');
    if (tabReg) tabReg.classList.add('active');
    if (formLogin) formLogin.style.display = 'none';
    if (formReg) formReg.style.display = 'block';
    if (title) title.innerHTML = '📝 ลงทะเบียนผู้ใช้งานใหม่';
    const regUser = document.getElementById('regUsername');
    if (regUser) setTimeout(() => regUser.focus(), 100);
  } else {
    if (tabReg) tabReg.classList.remove('active');
    if (tabLogin) tabLogin.classList.add('active');
    if (formReg) formReg.style.display = 'none';
    if (formLogin) formLogin.style.display = 'block';
    if (title) title.innerHTML = '🔐 เข้าสู่ระบบผู้ใช้งาน';
    const logUser = document.getElementById('loginUsername');
    if (logUser) setTimeout(() => logUser.focus(), 100);
  }
}

function openLoginModal(isMandatory = false, alertNotice = null) {
  isAuthModalMandatory = isMandatory;
  const modal = document.getElementById('loginModal');
  const btnClose = document.getElementById('btnCloseLoginModal');
  const btnCancel = document.getElementById('btnCancelLoginModal');
  const userInp = document.getElementById('loginUsername');
  const passInp = document.getElementById('loginPassword');
  const errMsg = document.getElementById('loginErrorMessage');
  const regErr = document.getElementById('registerErrorMessage');

  if (errMsg) {
    if (alertNotice) {
      errMsg.textContent = alertNotice;
      errMsg.style.display = 'block';
    } else {
      errMsg.style.display = 'none';
    }
  }
  if (regErr) regErr.style.display = 'none';
  if (userInp) userInp.value = '';
  if (passInp) passInp.value = '';

  const regUser = document.getElementById('regUsername');
  const regDisplay = document.getElementById('regDisplayName');
  const regPass = document.getElementById('regPassword');
  const regConfirm = document.getElementById('regConfirmPassword');
  if (regUser) regUser.value = '';
  if (regDisplay) regDisplay.value = '';
  if (regPass) regPass.value = '';
  if (regConfirm) regConfirm.value = '';

  if (isMandatory) {
    if (btnClose) btnClose.style.display = 'none';
    if (btnCancel) btnCancel.style.display = 'none';
  } else {
    if (btnClose) btnClose.style.display = 'inline-flex';
    if (btnCancel) btnCancel.style.display = 'inline-flex';
  }

  switchAuthTab('login');

  if (modal) {
    modal.classList.add('open');
    modal.setAttribute('aria-hidden', 'false');
    setTimeout(() => { if (userInp) userInp.focus(); }, 150);
  }
}

function closeLoginModal() {
  if (isAuthModalMandatory && !getCurrentUser()) {
    return;
  }
  const modal = document.getElementById('loginModal');
  if (modal) {
    modal.classList.remove('open');
    modal.setAttribute('aria-hidden', 'true');
  }
}

function openUserManagementModal() {
  const modal = document.getElementById('userManagementModal');
  if (!modal) return;
  fetchUsersCatalogFromCloud().then(() => renderUsersTable());
  renderUsersTable();
  modal.classList.add('open');
  modal.setAttribute('aria-hidden', 'false');
}

function closeUserManagementModal() {
  const modal = document.getElementById('userManagementModal');
  if (modal) {
    modal.classList.remove('open');
    modal.setAttribute('aria-hidden', 'true');
  }
}

function renderUsersTable(filterText = '') {
  const tbody = document.getElementById('usersTableBody');
  if (!tbody) return;
  tbody.innerHTML = '';

  const q = (filterText || '').trim().toLowerCase();
  const filtered = usersCatalogCache.filter(u => {
    if (!q) return true;
    return (u.username && u.username.toLowerCase().includes(q)) ||
           (u.display_name && u.display_name.toLowerCase().includes(q));
  });

  filtered.forEach(u => {
    const tr = document.createElement('tr');

    const roleBadge = u.role === 'admin'
      ? '<span class="badge-role-admin">👑 Admin</span>'
      : '<span class="badge-role-user">👩‍⚕️ User</span>';

    const slotText = u.user_slot === 0
      ? '<span style="font-weight:600; color:#0d9488;">เตียงหลัก (1-30)</span>'
      : `<span>Slot ${u.user_slot} (${(u.user_slot * 100) + 1} - ${(u.user_slot * 100) + 30})</span>`;

    let regBadge = '<span class="badge-reg-type">ระบบเดิม</span>';
    if (u.registered_via === 'app_self') {
      regBadge = '<span class="badge-reg-type badge-reg-app"><i class="fa-solid fa-desktop"></i> ลงทะเบียน (App)</span>';
    } else if (u.registered_via === 'web_self') {
      regBadge = '<span class="badge-reg-type badge-reg-web"><i class="fa-solid fa-globe"></i> ลงทะเบียน (Web)</span>';
    } else if (u.registered_via === 'admin_manual') {
      regBadge = '<span class="badge-reg-type badge-reg-admin"><i class="fa-solid fa-user-shield"></i> แอดมินสร้าง</span>';
    }

    let lastLoginText = '<span style="color:var(--text-muted); font-size:12px;">ยังไม่เคยเข้าใช้</span>';
    if (u.last_login_at) {
      try {
        const d = new Date(u.last_login_at);
        const pad = (n) => String(n).padStart(2, '0');
        const formattedDate = `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear() % 100} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
        lastLoginText = `<span style="font-size:12px;" title="${escapeHtml(u.last_login_at)}">${formattedDate}</span>`;
      } catch {
        lastLoginText = `<span style="font-size:12px;">${escapeHtml(u.last_login_at)}</span>`;
      }
    }

    const statusBadge = u.is_active !== false
      ? '<span class="badge-status-active"><i class="fa-solid fa-circle-check"></i> ใช้งาน</span>'
      : '<span class="badge-status-inactive"><i class="fa-solid fa-circle-xmark"></i> ปิดใช้งาน</span>';

    let actionBtns = '';
    // Switch to view bed
    actionBtns += `<button type="button" class="btn btn-secondary btn-sm" onclick="handleAdminSwitchToUser('${escapeHtml(u.username)}')" title="สลับดูเตียงของผู้ใช้นี้" style="margin-right:4px; padding:3px 7px; font-size:12px;">
      <i class="fa-solid fa-eye"></i> ดูเตียง
    </button>`;

    // Edit button
    actionBtns += `<button type="button" class="btn btn-secondary btn-sm" onclick="handleOpenEditUserModal('${escapeHtml(u.id || u.username)}')" title="แก้ไขข้อมูล" style="margin-right:4px; padding:3px 7px; font-size:12px;">
      <i class="fa-solid fa-pen"></i>
    </button>`;

    // Toggle active status & Delete button (not for primary admin)
    const isPrimaryRoot = (u.user_slot === 0 || u.id === 'u_admin');
    if (!isPrimaryRoot) {
      const isAct = u.is_active !== false;
      actionBtns += `<button type="button" class="btn btn-secondary btn-sm" onclick="handleToggleUserActive('${escapeHtml(u.username)}')" title="${isAct ? 'ระงับการใช้งาน' : 'เปิดใช้งาน'}" style="margin-right:4px; padding:3px 7px; font-size:12px; color:${isAct ? '#f59e0b' : '#10b981'};">
        <i class="fa-solid ${isAct ? 'fa-ban' : 'fa-check'}"></i>
      </button>`;

      actionBtns += `<button type="button" class="btn btn-secondary btn-sm" onclick="handleDeleteUser('${escapeHtml(u.username)}')" title="ลบผู้ใช้" style="padding:3px 7px; font-size:12px; color:#ef4444;">
        <i class="fa-solid fa-trash"></i>
      </button>`;
    }

    tr.innerHTML = `
      <td style="font-weight:600; font-family:var(--font-mono);">${escapeHtml(u.username)}</td>
      <td>${escapeHtml(u.display_name || u.username)}</td>
      <td>${roleBadge}</td>
      <td>${slotText}</td>
      <td>${regBadge}</td>
      <td>${lastLoginText}</td>
      <td>${statusBadge}</td>
      <td style="text-align:right; white-space:nowrap;">${actionBtns}</td>
    `;
    tbody.appendChild(tr);
  });
}

window.handleAdminSwitchToUser = function(username) {
  closeUserManagementModal();
  setActiveWorkspaceUser(username);
  showToast(`👀 กำลังเปิดดูเตียงของ ${username}`, 'info');
};

window.handleToggleUserActive = async function(username) {
  const u = usersCatalogCache.find(x => x.username.toLowerCase() === username.toLowerCase());
  if (!u) return;
  if (u.user_slot === 0 || u.id === 'u_admin') {
    alert('ไม่สามารถระงับบัญชีผู้ดูแลระบบหลักของวอร์ด (Admin Slot 0) ได้');
    return;
  }
  u.is_active = (u.is_active === false) ? true : false;
  await saveUsersCatalogToCloud(usersCatalogCache);
  updateWorkspaceSelectOptions();
  renderUsersTable(document.getElementById('userSearchInput')?.value || '');
  showToast(`${u.is_active ? '✅ เปิดใช้งาน' : '⛔ ระงับการใช้งาน'} บัญชี [${username}] แล้ว`, 'info');
};

window.handleDeleteUser = async function(username) {
  const u = usersCatalogCache.find(x => x.username.toLowerCase() === username.toLowerCase());
  if (!u) return;
  if (u.user_slot === 0 || u.id === 'u_admin') {
    alert('ไม่สามารถลบบัญชีผู้ดูแลระบบหลักของวอร์ด (Admin Slot 0) ได้');
    return;
  }

  if (!confirm(`ต้องการลบบัญชีผู้ใช้ [${username}] ออกจากระบบใช่หรือไม่?`)) {
    return;
  }

  const deleteBeds = confirm(`ต้องการล้างข้อมูลเตียง Slot ${u.user_slot} (${(u.user_slot * 100) + 1} - ${(u.user_slot * 100) + 30}) บน Cloud ด้วยหรือไม่?\n\nกด [OK / ตกลง] เพื่อล้างข้อมูลเตียงทั้งหมดของผู้ใช้นี้\nกด [Cancel / ยกเลิก] เพื่อลบเฉพาะบัญชีแต่เก็บประวัติเตียงไว้`);

  if (deleteBeds && u.user_slot > 0) {
    try {
      const minBed = (u.user_slot * 100) + 1;
      const maxBed = (u.user_slot * 100) + 30;
      await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=gte.${minBed}&bed_number=lte.${maxBed}`, {
        method: 'DELETE',
        headers: {
          'apikey': SUPABASE_KEY,
          'Authorization': `Bearer ${SUPABASE_KEY}`
        }
      });
    } catch (e) {
      console.warn('Clear user beds error:', e);
    }
  }

  usersCatalogCache = usersCatalogCache.filter(x => x.username.toLowerCase() !== username.toLowerCase());
  await saveUsersCatalogToCloud(usersCatalogCache);
  updateWorkspaceSelectOptions();
  renderUsersTable(document.getElementById('userSearchInput')?.value || '');
  showToast(`🗑️ ลบผู้ใช้ [${username}] เรียบร้อยแล้ว`, 'success');
};

window.handleOpenEditUserModal = function(idOrUsername) {
  const user = usersCatalogCache.find(u => u.id === idOrUsername || u.username === idOrUsername);
  if (!user) return;
  openAddEditUserModal(user);
};

function openAddEditUserModal(user = null) {
  const modal = document.getElementById('addEditUserModal');
  const title = document.getElementById('addEditUserTitle');
  const formId = document.getElementById('userFormId');
  const formUser = document.getElementById('userFormUsername');
  const formDisplay = document.getElementById('userFormDisplayName');
  const formRole = document.getElementById('userFormRole');
  const formPass = document.getElementById('userFormPassword');
  const formPassHint = document.getElementById('userFormPasswordHint');
  const formActive = document.getElementById('userFormIsActive');
  const errMsg = document.getElementById('userFormErrorMessage');

  if (errMsg) errMsg.style.display = 'none';
  if (formPass) formPass.value = '';

  if (user) {
    // Edit Mode
    if (title) title.innerHTML = `<i class="fa-solid fa-user-pen"></i> แก้ไขข้อมูลผู้ใช้: ${escapeHtml(user.username)}`;
    if (formId) formId.value = user.id || user.username;
    if (formUser) {
      formUser.value = user.username;
      formUser.disabled = false;
    }
    if (formDisplay) formDisplay.value = user.display_name || user.username;
    if (formRole) {
      formRole.value = user.role || 'user';
      formRole.disabled = (user.user_slot === 0 || user.id === 'u_admin');
    }
    if (formPassHint) formPassHint.textContent = '(เว้นว่างไว้หากไม่ต้องการเปลี่ยนรหัสผ่าน)';
    if (formActive) {
      formActive.checked = user.is_active !== false;
      formActive.disabled = (user.user_slot === 0 || user.id === 'u_admin');
    }
  } else {
    // Add Mode
    if (title) title.innerHTML = '<i class="fa-solid fa-user-plus"></i> เพิ่มผู้ใช้งานใหม่';
    if (formId) formId.value = '';
    if (formUser) {
      formUser.value = '';
      formUser.disabled = false;
    }
    if (formDisplay) formDisplay.value = '';
    if (formRole) {
      formRole.value = 'user';
      formRole.disabled = false;
    }
    if (formPassHint) formPassHint.textContent = '(ต้องระบุรหัสผ่านสำหรับการสร้างบัญชีใหม่)';
    if (formActive) {
      formActive.checked = true;
      formActive.disabled = false;
    }
  }

  if (modal) {
    modal.classList.add('open');
    modal.setAttribute('aria-hidden', 'false');
    setTimeout(() => {
      if (user) {
        if (formDisplay) formDisplay.focus();
      } else {
        if (formUser) formUser.focus();
      }
    }, 150);
  }
}

function closeAddEditUserModal() {
  const modal = document.getElementById('addEditUserModal');
  if (modal) {
    modal.classList.remove('open');
    modal.setAttribute('aria-hidden', 'true');
  }
}

async function saveAddEditUserSubmit() {
  const formId = document.getElementById('userFormId')?.value;
  const formUser = document.getElementById('userFormUsername')?.value.trim();
  const formDisplay = document.getElementById('userFormDisplayName')?.value.trim();
  const formRole = document.getElementById('userFormRole')?.value || 'user';
  const formPass = document.getElementById('userFormPassword')?.value || '';
  const formActive = document.getElementById('userFormIsActive')?.checked ?? true;
  const errMsg = document.getElementById('userFormErrorMessage');
  const btnSubmit = document.getElementById('btnSaveUserSubmit');

  if (errMsg) errMsg.style.display = 'none';

  if (!formUser) {
    if (errMsg) { errMsg.textContent = 'กรุณาระบุ Username'; errMsg.style.display = 'flex'; }
    return;
  }
  if (!formDisplay) {
    if (errMsg) { errMsg.textContent = 'กรุณาระบุชื่อแสดง (Display Name)'; errMsg.style.display = 'flex'; }
    return;
  }

  const isAdd = !formId;
  if (isAdd && !formPass) {
    if (errMsg) { errMsg.textContent = 'กรุณาระบุรหัสผ่านสำหรับผู้ใช้ใหม่'; errMsg.style.display = 'flex'; }
    return;
  }

  if (btnSubmit) {
    btnSubmit.disabled = true;
    btnSubmit.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังบันทึก...';
  }

  try {
    if (isAdd) {
      // Check duplicate
      const exists = usersCatalogCache.some(u => u.username.toLowerCase() === formUser.toLowerCase());
      if (exists) {
        if (errMsg) { errMsg.textContent = `Username "${formUser}" มีอยู่ในระบบแล้ว`; errMsg.style.display = 'flex'; }
        return;
      }

      // Find next slot (slot >= 2)
      let maxSlot = 1;
      usersCatalogCache.forEach(u => {
        if (u.user_slot && u.user_slot > maxSlot) maxSlot = u.user_slot;
      });
      const newSlot = Math.max(2, maxSlot + 1);
      const passHash = await sha256Hex(formPass);

      const newUser = {
        id: 'u_' + Date.now().toString(36),
        username: formUser,
        display_name: formDisplay,
        password_hash: passHash,
        role: formRole,
        user_slot: newSlot,
        is_active: formActive,
        registered_via: 'admin_manual',
        created_at: new Date().toISOString(),
        last_login_at: null
      };

      usersCatalogCache.push(newUser);
      await saveUsersCatalogToCloud(usersCatalogCache);
      await ensureUserSlotRowsExist(newSlot, formUser);

      showToast(`✅ สร้างผู้ใช้งาน [${formDisplay}] สำเร็จแล้ว (Slot ${newSlot})`, 'success');

    } else {
      // Edit
      const targetUser = usersCatalogCache.find(u => u.id === formId || u.username === formId || u.username.toLowerCase() === formUser.toLowerCase());
      if (!targetUser) {
        if (errMsg) { errMsg.textContent = 'ไม่พบผู้ใช้ในระบบ'; errMsg.style.display = 'flex'; }
        return;
      }

      // Check duplicate username across other users
      const duplicate = usersCatalogCache.find(u => u.id !== targetUser.id && u.username.toLowerCase() === formUser.toLowerCase());
      if (duplicate) {
        if (errMsg) { errMsg.textContent = `Username "${formUser}" มีผู้ใช้อื่นใช้งานอยู่แล้ว`; errMsg.style.display = 'flex'; }
        return;
      }

      const oldUsername = targetUser.username;
      targetUser.username = formUser;
      targetUser.display_name = formDisplay;
      targetUser.role = formRole;
      targetUser.is_active = formActive;

      if (formPass) {
        targetUser.password_hash = await sha256Hex(formPass);
      }

      // If active current user was updated, update local session
      const curUser = getCurrentUser();
      if (curUser && (curUser.id === targetUser.id || curUser.username.toLowerCase() === oldUsername.toLowerCase())) {
        curUser.username = targetUser.username;
        curUser.display_name = targetUser.display_name;
        curUser.role = targetUser.role;
        localStorage.setItem('ward_current_user', JSON.stringify(curUser));
      }

      await saveUsersCatalogToCloud(usersCatalogCache);
      showToast(`✏️ อัปเดตข้อมูลผู้ใช้ [${formDisplay}] เรียบร้อยแล้ว`, 'success');
    }

    closeAddEditUserModal();
    updateWorkspaceSelectOptions();
    renderUsersTable(document.getElementById('userSearchInput')?.value || '');

  } catch (err) {
    console.error('Save user error:', err);
    if (errMsg) { errMsg.textContent = 'บันทึกล้มเหลว กรุณาลองใหม่อีกครั้ง'; errMsg.style.display = 'flex'; }
  } finally {
    if (btnSubmit) {
      btnSubmit.disabled = false;
      btnSubmit.innerHTML = '<i class="fa-solid fa-floppy-disk"></i> บันทึกข้อมูล';
    }
  }
}

// ==========================================
// Event Listeners
// ==========================================
function setupEventListeners() {
  themeToggleBtn.addEventListener('click', toggleTheme);
  
  refreshBtn.addEventListener('click', () => {
    if (!getCurrentUser()) {
      openLoginModal(true);
      return;
    }
    resetIdleTimer();
    refreshBtn.classList.add('fa-spin');
    fetchAllBeds().finally(() => {
      setTimeout(() => refreshBtn.classList.remove('fa-spin'), 600);
    });
  });

  // Search
  searchInput.addEventListener('input', (e) => {
    currentSearch = e.target.value.trim().toLowerCase();
    clearSearchBtn.style.display = currentSearch ? 'block' : 'none';
    renderBeds();
  });

  clearSearchBtn.addEventListener('click', () => {
    searchInput.value = '';
    currentSearch = '';
    clearSearchBtn.style.display = 'none';
    searchInput.focus();
    renderBeds();
  });

  // Filter Tabs
  tabButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      tabButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      currentFilter = btn.dataset.filter;
      renderBeds();
    });
  });

  // Edit Modal Safe Handlers
  modalCloseBtn.addEventListener('click', safeCloseEditModal);
  btnCancelEdit.addEventListener('click', safeCloseEditModal);
  
  // Track mousedown to ensure clicks originate on backdrop and not dragged from inside the modal card
  editModal.addEventListener('mousedown', (e) => {
    editModalMouseDownOnBackdrop = (e.target === editModal);
  });

  editModal.addEventListener('click', (e) => {
    // Only handle if both mousedown and click occurred strictly on backdrop
    if (e.target === editModal && editModalMouseDownOnBackdrop) {
      if (hasUnsavedEditChanges()) {
        shakeEditModal();
        showToast('⚠️ กำลังแก้ไขข้อมูลเตียงนี้อยู่ กรุณากด "บันทึกลง Cloud" หรือกด "ยกเลิก"', 'warning');
      } else {
        closeEditModal();
      }
    }
    editModalMouseDownOnBackdrop = false;
  });

  noteTextarea.addEventListener('input', () => {
    updateCharCount();
    saveEditDraft();
  });

  // Quick Tags
  quickTagBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      const snippet = btn.dataset.snippet.replace(/\n/g, '\n');
      insertSnippet(snippet);
    });
  });

  btnSaveBedNote.addEventListener('click', saveCurrentBed);
  btnClearBedNote.addEventListener('click', clearCurrentBed);
  btnViewHistoryFromModal.addEventListener('click', () => {
    if (hasUnsavedEditChanges()) {
      if (!confirm('มีข้อความที่กำลังแก้ไขและยังไม่ได้บันทึก ต้องการเปิดดูประวัติย้อนหลังโดยละทิ้งข้อความใช่หรือไม่?')) {
        return;
      }
      clearEditDraft(activeBedNumber);
    }
    closeEditModal();
    openHistoryModal(activeBedNumber);
  });

  // History Modal
  historyCloseBtn.addEventListener('click', closeHistoryModal);
  btnCloseHistory.addEventListener('click', closeHistoryModal);
  historyModal.addEventListener('click', (e) => {
    if (e.target === historyModal) closeHistoryModal();
  });

  btnCopyHistoryText.addEventListener('click', () => {
    if (historyPreviewText.value) {
      navigator.clipboard.writeText(historyPreviewText.value);
      showToast('คัดลอกข้อความประวัติเรียบร้อย', 'success');
    }
  });

  btnRestoreFromHistory.addEventListener('click', restoreSelectedHistory);

  // Swap/Move Modal Listeners
  const btnSwapBedFromModal = document.getElementById('btnSwapBedFromModal');
  if (btnSwapBedFromModal) {
    btnSwapBedFromModal.addEventListener('click', () => {
      const curBed = bedsData.find(b => b.bed_number === activeBedNumber);
      if (curBed) {
        curBed.content = noteTextarea.value;
      }
      saveEditDraft();
      closeEditModal();
      openSwapModal(activeBedNumber);
    });
  }

  const swapCloseBtn = document.getElementById('swapCloseBtn');
  const btnCancelSwap = document.getElementById('btnCancelSwap');
  const btnConfirmSwap = document.getElementById('btnConfirmSwap');
  const swapModal = document.getElementById('swapModal');
  const swapTargetSelect = document.getElementById('swapTargetSelect');

  if (swapCloseBtn) swapCloseBtn.addEventListener('click', closeSwapModal);
  if (btnCancelSwap) btnCancelSwap.addEventListener('click', closeSwapModal);
  if (swapModal) {
    swapModal.addEventListener('click', (e) => {
      if (e.target === swapModal) closeSwapModal();
    });
  }
  if (btnConfirmSwap) btnConfirmSwap.addEventListener('click', executeBedSwap);
  if (swapTargetSelect) {
    swapTargetSelect.addEventListener('change', handleTargetBedChanged);
  }

  // Workspace Switcher & User Account Listeners (v1.8.0)
  const wsSelect = document.getElementById('workspaceSelect');
  if (wsSelect) {
    wsSelect.addEventListener('change', (e) => {
      setActiveWorkspaceUser(e.target.value);
      showToast(`🔄 สลับพื้นที่งานไปยัง: ${e.target.selectedOptions[0]?.textContent || e.target.value}`, 'info');
    });
  }

  const btnBackMyAdmin = document.getElementById('btnBackToMyAdminWorkspace');
  if (btnBackMyAdmin) {
    btnBackMyAdmin.addEventListener('click', () => {
      setActiveWorkspaceUser('admin');
      showToast('👑 สลับกลับมายังเตียงหลักของ Admin เรียบร้อยแล้ว', 'info');
    });
  }

  const userBadgeBtn = document.getElementById('userBadgeBtn');
  const userDropdownMenu = document.getElementById('userDropdownMenu');
  if (userBadgeBtn && userDropdownMenu) {
    userBadgeBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      const isShowing = userDropdownMenu.style.display === 'flex';
      userDropdownMenu.style.display = isShowing ? 'none' : 'flex';
      userBadgeBtn.classList.toggle('open', !isShowing);
    });
    document.addEventListener('click', (e) => {
      if (!userDropdownMenu.contains(e.target) && !userBadgeBtn.contains(e.target)) {
        userDropdownMenu.style.display = 'none';
        userBadgeBtn.classList.remove('open');
      }
    });
  }

  const btnOpenUserMgmt = document.getElementById('btnOpenUserManagement');
  if (btnOpenUserMgmt) {
    btnOpenUserMgmt.addEventListener('click', () => {
      if (userDropdownMenu) userDropdownMenu.style.display = 'none';
      if (userBadgeBtn) userBadgeBtn.classList.remove('open');
      openUserManagementModal();
    });
  }

  const btnOpenLogin = document.getElementById('btnOpenLoginModal');
  if (btnOpenLogin) {
    btnOpenLogin.addEventListener('click', () => {
      if (userDropdownMenu) userDropdownMenu.style.display = 'none';
      if (userBadgeBtn) userBadgeBtn.classList.remove('open');
      openLoginModal(false);
    });
  }

  const btnLogout = document.getElementById('btnLogoutUser');
  if (btnLogout) {
    btnLogout.addEventListener('click', () => {
      if (confirm('ต้องการออกจากระบบใช่หรือไม่?')) {
        if (userDropdownMenu) userDropdownMenu.style.display = 'none';
        if (userBadgeBtn) userBadgeBtn.classList.remove('open');
        clearCurrentUser();
      }
    });
  }

  // Login & Register Modal Controls
  const btnCloseLogin = document.getElementById('btnCloseLoginModal');
  const btnCancelLogin = document.getElementById('btnCancelLoginModal');
  if (btnCloseLogin) btnCloseLogin.addEventListener('click', closeLoginModal);
  if (btnCancelLogin) btnCancelLogin.addEventListener('click', closeLoginModal);

  const loginModal = document.getElementById('loginModal');
  if (loginModal) {
    loginModal.addEventListener('click', (e) => {
      if (e.target === loginModal && (!isAuthModalMandatory || getCurrentUser())) closeLoginModal();
    });
  }

  const tabLogin = document.getElementById('tabBtnLogin');
  const tabReg = document.getElementById('tabBtnRegister');
  if (tabLogin) tabLogin.addEventListener('click', () => switchAuthTab('login'));
  if (tabReg) tabReg.addEventListener('click', () => switchAuthTab('register'));

  const loginShowPass = document.getElementById('loginShowPassword');
  if (loginShowPass) {
    loginShowPass.addEventListener('change', (e) => {
      const passInp = document.getElementById('loginPassword');
      if (passInp) passInp.type = e.target.checked ? 'text' : 'password';
    });
  }

  const regShowPass = document.getElementById('regShowPassword');
  if (regShowPass) {
    regShowPass.addEventListener('change', (e) => {
      const p1 = document.getElementById('regPassword');
      const p2 = document.getElementById('regConfirmPassword');
      const t = e.target.checked ? 'text' : 'password';
      if (p1) p1.type = t;
      if (p2) p2.type = t;
    });
  }

  // Login Form Submit
  const loginForm = document.getElementById('loginForm');
  if (loginForm) {
    loginForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const userInp = document.getElementById('loginUsername');
      const passInp = document.getElementById('loginPassword');
      const errMsg = document.getElementById('loginErrorMessage');
      const btnSubmit = document.getElementById('btnLoginSubmit');
      const rememberMe = document.getElementById('loginRememberMe')?.checked ?? true;

      const username = userInp?.value.trim().toLowerCase();
      const password = passInp?.value || '';

      if (!username || !password) {
        if (errMsg) { errMsg.textContent = 'กรุณาระบุ Username และ Password ให้ครบถ้วน'; errMsg.style.display = 'flex'; }
        return;
      }

      if (btnSubmit) {
        btnSubmit.disabled = true;
        btnSubmit.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังเข้าสู่ระบบ...';
      }

      try {
        await fetchUsersCatalogFromCloud();
        const enteredHash = await sha256Hex(password);
        let matchedUser = usersCatalogCache.find(u => u.username.toLowerCase() === username);

        if (!matchedUser || matchedUser.password_hash !== enteredHash) {
          if (errMsg) { errMsg.textContent = '❌ ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง'; errMsg.style.display = 'flex'; }
          return;
        }

        if (matchedUser.is_active === false) {
          if (errMsg) { errMsg.textContent = '⚠️ บัญชีนี้ถูกปิดการใช้งาน กรุณาติดต่อ Admin'; errMsg.style.display = 'flex'; }
          return;
        }

        matchedUser.last_login_at = new Date().toISOString();
        saveUsersCatalogToCloud(usersCatalogCache);

        setCurrentUser(matchedUser, rememberMe);
        if (matchedUser.role === 'admin') {
          setActiveWorkspaceUser('admin');
        } else {
          localStorage.removeItem('ward_admin_active_workspace');
        }

        closeLoginModal();
        showToast(`👋 ยินดีต้อนรับ ${matchedUser.display_name}!`, 'success');
        initAuthorName();
        initEmptyBeds();
        loadCachedBeds();
        fetchAllBeds();
        if (!pollTimer) {
          pollTimer = setInterval(fetchAllBeds, 15000);
        }

      } catch (err) {
        console.error('Login error:', err);
        if (errMsg) { errMsg.textContent = 'เกิดข้อผิดพลาดในการเชื่อมต่อ กรุณาลองใหม่อีกครั้ง'; errMsg.style.display = 'flex'; }
      } finally {
        if (btnSubmit) {
          btnSubmit.disabled = false;
          btnSubmit.innerHTML = '<i class="fa-solid fa-arrow-right-to-bracket"></i> เข้าสู่ระบบ';
        }
      }
    });
  }

  // Register Form Submit
  const regForm = document.getElementById('registerForm');
  if (regForm) {
    regForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const regUserInp = document.getElementById('regUsername');
      const regDisplayInp = document.getElementById('regDisplayName');
      const regPassInp = document.getElementById('regPassword');
      const regConfirmInp = document.getElementById('regConfirmPassword');
      const regErrMsg = document.getElementById('registerErrorMessage');
      const btnSubmit = document.getElementById('btnRegisterSubmit');

      const username = regUserInp?.value.trim().toLowerCase();
      const displayName = regDisplayInp?.value.trim();
      const password = regPassInp?.value || '';
      const confirmPass = regConfirmInp?.value || '';

      if (!username || !displayName || !password || !confirmPass) {
        if (regErrMsg) { regErrMsg.textContent = 'กรุณากรอกข้อมูลให้ครบทุกช่อง'; regErrMsg.style.display = 'flex'; }
        return;
      }

      if (!/^[a-zA-Z0-9_]{3,20}$/.test(username)) {
        if (regErrMsg) { regErrMsg.textContent = 'Username ต้องเป็นตัวอักษร a-z, 0-9 หรือ _ และมีความยาว 3-20 ตัวอักษร'; regErrMsg.style.display = 'flex'; }
        return;
      }

      if (password.length < 4) {
        if (regErrMsg) { regErrMsg.textContent = 'รหัสผ่านต้องมีความยาวอย่างน้อย 4 ตัวอักษร'; regErrMsg.style.display = 'flex'; }
        return;
      }

      if (password !== confirmPass) {
        if (regErrMsg) { regErrMsg.textContent = 'รหัสผ่านและการยืนยันรหัสผ่านไม่ตรงกัน'; regErrMsg.style.display = 'flex'; }
        return;
      }

      if (btnSubmit) {
        btnSubmit.disabled = true;
        btnSubmit.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังลงทะเบียน...';
      }

      try {
        await fetchUsersCatalogFromCloud();
        const exists = usersCatalogCache.some(u => u.username.toLowerCase() === username);
        if (exists) {
          if (regErrMsg) { regErrMsg.textContent = `ชื่อผู้ใช้ "${username}" มีอยู่ในระบบแล้ว กรุณาใช้ชื่ออื่น`; regErrMsg.style.display = 'flex'; }
          return;
        }

        let maxSlot = 1;
        usersCatalogCache.forEach(u => {
          if (u.user_slot && u.user_slot > maxSlot) maxSlot = u.user_slot;
        });
        const newSlot = Math.max(2, maxSlot + 1);
        const passHash = await sha256Hex(password);

        const newUser = {
          id: 'u_' + Date.now().toString(36),
          username: username,
          display_name: displayName,
          password_hash: passHash,
          role: 'user',
          user_slot: newSlot,
          is_active: true,
          registered_via: 'web_self',
          created_at: new Date().toISOString(),
          last_login_at: new Date().toISOString()
        };

        usersCatalogCache.push(newUser);
        await saveUsersCatalogToCloud(usersCatalogCache);
        await ensureUserSlotRowsExist(newSlot, username);

        setCurrentUser(newUser, true);
        localStorage.removeItem('ward_admin_active_workspace');

        closeLoginModal();
        showToast(`🎉 ยินดีต้อนรับ ${displayName}! ลงทะเบียนและเข้าใช้งานเรียบร้อย`, 'success');
        initAuthorName();
        initEmptyBeds();
        loadCachedBeds();
        fetchAllBeds();
        if (!pollTimer) {
          pollTimer = setInterval(fetchAllBeds, 15000);
        }

      } catch (err) {
        console.error('Registration error:', err);
        if (regErrMsg) { regErrMsg.textContent = 'เกิดข้อผิดพลาดในการลงทะเบียน กรุณาลองใหม่อีกครั้ง'; regErrMsg.style.display = 'flex'; }
      } finally {
        if (btnSubmit) {
          btnSubmit.disabled = false;
          btnSubmit.innerHTML = '<i class="fa-solid fa-user-plus"></i> ลงทะเบียนและเข้าใช้งานทันที';
        }
      }
    });
  }

  // User Management Modal
  const btnCloseUserMgmt = document.getElementById('btnCloseUserManagementModal');
  const btnDismissUserMgmt = document.getElementById('btnDismissUserManagement');
  if (btnCloseUserMgmt) btnCloseUserMgmt.addEventListener('click', closeUserManagementModal);
  if (btnDismissUserMgmt) btnDismissUserMgmt.addEventListener('click', closeUserManagementModal);

  const userMgmtModal = document.getElementById('userManagementModal');
  if (userMgmtModal) {
    userMgmtModal.addEventListener('click', (e) => {
      if (e.target === userMgmtModal) closeUserManagementModal();
    });
  }

  const btnOpenAddUser = document.getElementById('btnOpenAddUserModal');
  if (btnOpenAddUser) btnOpenAddUser.addEventListener('click', () => openAddEditUserModal(null));

  const userSearchInp = document.getElementById('userSearchInput');
  if (userSearchInp) {
    userSearchInp.addEventListener('input', (e) => {
      renderUsersTable(e.target.value);
    });
  }

  // Add / Edit User Modal
  const btnCloseAddEdit = document.getElementById('btnCloseAddEditUserModal');
  const btnCancelAddEdit = document.getElementById('btnCancelAddEditUser');
  if (btnCloseAddEdit) btnCloseAddEdit.addEventListener('click', closeAddEditUserModal);
  if (btnCancelAddEdit) btnCancelAddEdit.addEventListener('click', closeAddEditUserModal);

  const addEditModal = document.getElementById('addEditUserModal');
  if (addEditModal) {
    addEditModal.addEventListener('click', (e) => {
      if (e.target === addEditModal) closeAddEditUserModal();
    });
  }

  const userFormShowPass = document.getElementById('userFormShowPassword');
  if (userFormShowPass) {
    userFormShowPass.addEventListener('change', (e) => {
      const passInp = document.getElementById('userFormPassword');
      if (passInp) passInp.type = e.target.checked ? 'text' : 'password';
    });
  }

  const btnSaveUser = document.getElementById('btnSaveUserSubmit');
  if (btnSaveUser) btnSaveUser.addEventListener('click', saveAddEditUserSubmit);

  // View Mode Toggle (Compact vs Expanded)
  initViewMode();

  // Keyboard Shortcuts
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      if (addEditModal && addEditModal.classList.contains('open')) {
        closeAddEditUserModal();
      } else if (userMgmtModal && userMgmtModal.classList.contains('open')) {
        closeUserManagementModal();
      } else if (loginModal && loginModal.classList.contains('open')) {
        if (!isAuthModalMandatory || getCurrentUser()) {
          closeLoginModal();
        }
      } else if (templateModal && templateModal.classList.contains('open')) {
        closeTemplateModal();
      } else if (document.getElementById('docsModal') && document.getElementById('docsModal').classList.contains('open')) {
        closeDocsModal();
      } else if (document.getElementById('ioModal') && document.getElementById('ioModal').classList.contains('open')) {
        closeIoModal();
      } else if (editModal && editModal.classList.contains('open')) {
        safeCloseEditModal();
      } else if (historyModal && historyModal.classList.contains('open')) {
        closeHistoryModal();
      } else if (swapModal && swapModal.classList.contains('open')) {
        closeSwapModal();
      }
    }
  });

  // Protect against accidental page refresh / navigation while editing
  window.addEventListener('beforeunload', (e) => {
    if (hasUnsavedEditChanges()) {
      e.preventDefault();
      e.returnValue = '';
    }
  });
}

// ==========================================
// Supabase API Integration
// ==========================================
async function fetchAllBeds() {
  if (!getCurrentUser() || document.hidden || bedFetchBusy || Date.now() < bedRetryAt) return;
  bedFetchBusy = true;
  const generation = bedGeneration;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 12000);
  try {
    const slot = getActiveWorkspaceSlot();
    const startBed = slot === 0 ? 1 : (slot * 100) + 1;
    const endBed = slot === 0 ? 30 : (slot * 100) + 30;

    const meta = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=gte.${startBed}&bed_number=lte.${endBed}&select=bed_number,updated_at&order=bed_number.asc`, {
      signal: controller.signal,
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });

    if (!meta.ok) throw new Error(`HTTP ${meta.status}`);

    const version = JSON.stringify(await meta.json());
    if (generation !== bedGeneration || !getCurrentUser() || slot !== getActiveWorkspaceSlot()) return;

    if (version === bedVersion && Date.now() - bedFullFetchAt < 300000) {
      bedFailures = 0;
      bedRetryAt = 0;
      setCloudStatus(true);
      return;
    }

    const res = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=gte.${startBed}&bed_number=lte.${endBed}&select=bed_number,content,updated_at,updated_by&order=bed_number.asc`, {
      method: 'GET',
      signal: controller.signal,
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });

    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();
    if (generation !== bedGeneration || !getCurrentUser() || slot !== getActiveWorkspaceSlot()) return;
    bedVersion = version;
    bedFullFetchAt = Date.now();
    bedFailures = 0;
    bedRetryAt = 0;
    setCloudStatus(true);

    if (slot > 0 && data.length < 30) {
      ensureUserSlotRowsExist(slot, getActiveWorkspaceUser());
    }

    // Track changed beds for pulse animation
    const changedBeds = new Set();
    data.forEach(item => {
      const localBedNum = slot === 0 ? item.bed_number : item.bed_number - (slot * 100);
      if (localBedNum >= 1 && localBedNum <= 30) {
        const prev = prevContentMap.get(localBedNum);
        if (prev !== undefined && prev !== (item.content || '')) {
          changedBeds.add(localBedNum);
        }
        prevContentMap.set(localBedNum, item.content || '');

        const idx = bedsData.findIndex(b => b.bed_number === localBedNum);
        if (idx !== -1) {
          bedsData[idx] = {
            bed_number: localBedNum,
            content: item.content || '',
            updated_at: item.updated_at,
            updated_by: item.updated_by || ''
          };
        }
      }
    });

    // Save to localStorage cache for current workspace
    try {
      const wsUser = getActiveWorkspaceUser() || 'admin';
      localStorage.setItem('ward_bed_notes_cache_' + wsUser, JSON.stringify(bedsData));
      if (slot === 0) {
        localStorage.setItem('ward_bed_notes_cache', JSON.stringify(bedsData));
      }
    } catch {}

    renderBeds(changedBeds);
    updateStats();

  } catch (err) {
    console.warn('Supabase fetch error:', err);
    if (generation === bedGeneration && getCurrentUser()) {
      bedRetryAt = Date.now() + Math.min(120000, 15000 * Math.pow(2, bedFailures++));
      setCloudStatus(false);
    }
  } finally {
    clearTimeout(timeout);
    bedFetchBusy = false;
    if (generation !== bedGeneration && getCurrentUser()) fetchAllBeds();
  }
}

function setCloudStatus(online) {
  if (online) {
    cloudStatusBadge.className = 'status-badge online';
    cloudStatusText.textContent = 'ออนไลน์ (Cloud สด)';
  } else {
    cloudStatusBadge.className = 'status-badge offline';
    cloudStatusText.textContent = 'ออฟไลน์ (กำลังต่อใหม่)';
  }
}

// ==========================================
// Render Beds Grid
// ==========================================
function renderBeds(changedBeds = new Set()) {
  if (!getCurrentUser()) {
    if (statOccupiedBeds) statOccupiedBeds.textContent = '-';
    if (statAvailableBeds) statAvailableBeds.textContent = '-';
    if (statLastSyncTime) statLastSyncTime.textContent = '--:--:--';
    bedsGrid.innerHTML = `
      <div class="grid-loading" style="grid-column: 1 / -1; padding: 60px 20px; text-align: center;">
        <i class="fa-solid fa-lock" style="font-size: 48px; color: #0d9488; margin-bottom: 16px;"></i>
        <h3 style="font-size: 18px; margin-bottom: 8px; color: var(--text-primary);">ระบบถูกล็อกเพื่อความปลอดภัย</h3>
        <p style="color: var(--text-muted); margin-bottom: 18px;">กรุณาเข้าสู่ระบบเพื่อดูและจัดการข้อมูลผู้ป่วยรายเตียง</p>
        <button type="button" class="btn btn-primary" onclick="openLoginModal(true)" style="padding: 10px 24px; font-weight: 600;">
          <i class="fa-solid fa-arrow-right-to-bracket"></i> เข้าสู่ระบบเดี๋ยวนี้
        </button>
      </div>`;
    return;
  }

  const filtered = bedsData.filter(bed => {
    // Filter Tab
    const isOccupied = bed.content && bed.content.trim().length > 0;
    if (currentFilter === 'occupied' && !isOccupied) return false;
    if (currentFilter === 'available' && isOccupied) return false;

    // Search Query
    if (currentSearch) {
      const matchBedNum = `เตียง ${bed.bed_number}`.toLowerCase().includes(currentSearch) ||
                          `bed ${bed.bed_number}`.toLowerCase().includes(currentSearch) ||
                          `${bed.bed_number}` === currentSearch;
      const matchContent = (bed.content || '').toLowerCase().includes(currentSearch);
      const matchAuthor = (bed.updated_by || '').toLowerCase().includes(currentSearch);
      return matchBedNum || matchContent || matchAuthor;
    }

    return true;
  });

  if (filtered.length === 0) {
    bedsGrid.innerHTML = `
      <div class="grid-loading">
        <i class="fa-regular fa-folder-open" style="font-size: 38px; color: var(--text-dim); margin-bottom: 12px;"></i>
        <p>ไม่พบเตียงที่ตรงกับเงื่อนไขการค้นหา</p>
      </div>`;
    return;
  }

  bedsGrid.innerHTML = '';
  filtered.forEach(bed => {
    const isOccupied = bed.content && bed.content.trim().length > 0;
    const isPulse = changedBeds.has(bed.bed_number);

    const card = document.createElement('div');
    card.className = `bed-card ${isOccupied ? 'occupied' : 'empty'} ${isPulse ? 'live-pulse' : ''}`;
    card.dataset.bed = bed.bed_number;

    const timeAgo = formatTimeAgo(bed.updated_at);
    const author = bed.updated_by ? ` • โดย ${escapeHtml(bed.updated_by)}` : '';

    const contentDisplay = isOccupied 
      ? escapeHtml(bed.content.trim()) 
      : '<i class="fa-solid fa-bed" style="margin-right: 6px;"></i> เตียงว่าง (แตะเพื่อบันทึกข้อมูล)';

    const hasOverflow = isOccupied && (bed.content.length > 120 || bed.content.split('\n').length > 4);

    card.innerHTML = `
      <div class="bed-card-header">
        <div class="bed-badge">
          <span class="bed-drag-handle" title="ลากเพื่อสลับหรือย้ายเตียง"><i class="fa-solid fa-grip-vertical"></i></span>
          <span class="bed-number-pill">เตียง ${String(bed.bed_number).padStart(2, '0')}</span>
        </div>
        <span class="bed-status-pill ${isOccupied ? 'occupied' : 'empty'}">
          ${isOccupied ? '● มีผู้ป่วย' : '○ ว่าง'}
        </span>
      </div>

      <div class="bed-card-body" onclick="openEditModal(${bed.bed_number})">
        <div class="bed-note-content ${hasOverflow ? 'has-overflow' : ''}">${contentDisplay}</div>
        <div class="bed-card-meta">
          <span><i class="fa-regular fa-clock"></i> ${timeAgo}${author}</span>
          ${isOccupied ? `<span>${bed.content.length} ตัวอักษร</span>` : ''}
        </div>
      </div>

      <div class="bed-card-footer">
        <div style="display: flex; gap: 4px;">
          <button type="button" class="btn-card-action" onclick="openHistoryModal(${bed.bed_number})" title="ดูประวัติเตียง">
            <i class="fa-solid fa-clock-rotate-left"></i> ประวัติ
          </button>
          <button type="button" class="btn-card-action btn-card-swap" onclick="openSwapModal(${bed.bed_number})" title="สลับ/ย้ายเตียง">
            <i class="fa-solid fa-arrow-right-arrow-left"></i> ย้าย/สลับ
          </button>
        </div>
        <div style="display: flex; gap: 4px;">
          ${isOccupied ? `
            <button type="button" class="btn-card-action" onclick="copyBedContent(${bed.bed_number})" title="คัดลอกข้อความ">
              <i class="fa-regular fa-copy"></i> คัดลอก
            </button>` : ''}
          <button type="button" class="btn-card-action btn-card-primary" onclick="openEditModal(${bed.bed_number})">
            <i class="fa-solid fa-pen"></i> ${isOccupied ? 'แก้ไข' : 'ลงบันทึก'}
          </button>
        </div>
      </div>
    `;

    // Enable Drag and Drop
    card.setAttribute('draggable', 'true');
    card.addEventListener('dragstart', (e) => {
      e.dataTransfer.setData('text/plain', String(bed.bed_number));
      e.dataTransfer.effectAllowed = 'move';
      card.classList.add('is-dragging');
    });

    card.addEventListener('dragend', () => {
      card.classList.remove('is-dragging');
      document.querySelectorAll('.bed-card.drag-over').forEach(c => c.classList.remove('drag-over'));
    });

    card.addEventListener('dragover', (e) => {
      e.preventDefault();
      e.dataTransfer.dropEffect = 'move';
      card.classList.add('drag-over');
    });

    card.addEventListener('dragleave', (e) => {
      if (!card.contains(e.relatedTarget)) {
        card.classList.remove('drag-over');
      }
    });

    card.addEventListener('drop', (e) => {
      e.preventDefault();
      card.classList.remove('drag-over');
      const rawSrc = e.dataTransfer.getData('text/plain');
      const sourceBedNum = parseInt(rawSrc, 10);
      const targetBedNum = bed.bed_number;
      if (sourceBedNum && targetBedNum && sourceBedNum !== targetBedNum) {
        openSwapModal(sourceBedNum, targetBedNum);
      }
    });

    // Touch Drag support for iPad & Mobile Tablets
    const dragHandle = card.querySelector('.bed-drag-handle');
    if (dragHandle) {
      let touchActive = false;
      let currentHoverCard = null;

      dragHandle.addEventListener('touchstart', (e) => {
        touchActive = true;
        card.classList.add('is-dragging');
      }, { passive: true });

      dragHandle.addEventListener('touchmove', (e) => {
        if (!touchActive) return;
        const touch = e.touches[0];
        const elem = document.elementFromPoint(touch.clientX, touch.clientY);
        const targetCard = elem ? elem.closest('.bed-card') : null;

        if (currentHoverCard && currentHoverCard !== targetCard) {
          currentHoverCard.classList.remove('drag-over');
        }

        if (targetCard && targetCard !== card) {
          targetCard.classList.add('drag-over');
          currentHoverCard = targetCard;
        } else {
          currentHoverCard = null;
        }
      }, { passive: true });

      dragHandle.addEventListener('touchend', () => {
        if (!touchActive) return;
        touchActive = false;
        card.classList.remove('is-dragging');
        if (currentHoverCard) {
          currentHoverCard.classList.remove('drag-over');
          const targetBedNum = parseInt(currentHoverCard.dataset.bed, 10);
          if (targetBedNum && targetBedNum !== bed.bed_number) {
            openSwapModal(bed.bed_number, targetBedNum);
          }
          currentHoverCard = null;
        }
      });
    }

    bedsGrid.appendChild(card);
  });
}

// Update Top Bar Census & Stats
function updateStats() {
  const total = 30;
  const occupied = bedsData.filter(b => b.content && b.content.trim().length > 0).length;
  const available = total - occupied;

  statTotalBeds.textContent = total;
  statOccupiedBeds.textContent = occupied;
  statAvailableBeds.textContent = available;
  statLastSyncTime.textContent = new Date().toLocaleTimeString('th-TH');

  countAll.textContent = total;
  countOccupied.textContent = occupied;
  countAvailable.textContent = available;
}

// ==========================================
// Edit Note Modal & Safe Editing System
// ==========================================
let originalEditContent = '';
let editModalMouseDownOnBackdrop = false;

function hasUnsavedEditChanges() {
  if (!editModal || !editModal.classList.contains('open')) return false;
  const currentVal = (noteTextarea.value || '').trim();
  const origVal = (originalEditContent || '').trim();
  return currentVal !== origVal;
}

function getDraftStorageKey(bedNum) {
  const wsUser = getActiveWorkspaceUser() || 'admin';
  return `ward_bed_draft_${wsUser.toLowerCase()}_${bedNum}`;
}

function saveEditDraft(bedNum) {
  const b = bedNum || activeBedNumber;
  if (!b) return;
  try {
    const key = getDraftStorageKey(b);
    const val = noteTextarea.value;
    if (val !== originalEditContent && val.trim().length > 0) {
      localStorage.setItem(key, val);
    } else {
      localStorage.removeItem(key);
    }
  } catch {}
}

function clearEditDraft(bedNum) {
  const b = bedNum || activeBedNumber;
  if (!b) return;
  try {
    localStorage.removeItem(getDraftStorageKey(b));
  } catch {}
}

function shakeEditModal() {
  const card = editModal ? editModal.querySelector('.modal-card') : null;
  if (card) {
    card.classList.remove('shake');
    void card.offsetWidth; // Force CSS animation reflow
    card.classList.add('shake');
    setTimeout(() => {
      if (card) card.classList.remove('shake');
    }, 400);
  }
}

function safeCloseEditModal() {
  if (hasUnsavedEditChanges()) {
    const confirmDiscard = confirm('⚠️ มีข้อความที่กำลังแก้ไขและยังไม่ได้บันทึกลง Cloud\n\nต้องการยกเลิกและละทิ้งข้อความที่กำลังพิมพ์ใช่หรือไม่?');
    if (!confirmDiscard) {
      return false;
    }
    clearEditDraft(activeBedNumber);
  }
  closeEditModal();
  return true;
}

window.openEditModal = function(bedNum) {
  if (!getCurrentUser()) {
    openLoginModal(true, 'กรุณาเข้าสู่ระบบก่อนดูหรือแก้ไขข้อมูลผู้ป่วย');
    return;
  }
  resetIdleTimer();

  activeBedNumber = bedNum;
  const bed = bedsData.find(b => b.bed_number === bedNum) || { content: '', updated_by: '' };

  modalBedBadge.textContent = `เตียง ${String(bedNum).padStart(2, '0')}`;
  modalTitle.textContent = bed.content && bed.content.trim() ? `แก้ไขข้อมูลผู้ป่วย เตียง ${bedNum}` : `ลงบันทึกข้อมูลใหม่ เตียง ${bedNum}`;
  
  const serverContent = bed.content || '';
  originalEditContent = serverContent;

  // Check if an unsaved draft exists
  let initialContent = serverContent;
  let hasDraft = false;
  try {
    const draft = localStorage.getItem(getDraftStorageKey(bedNum));
    if (draft !== null && draft !== serverContent && draft.trim().length > 0) {
      initialContent = draft;
      hasDraft = true;
    }
  } catch {}

  noteTextarea.value = initialContent;
  
  if (bed.updated_by) {
    authorInput.value = bed.updated_by;
  } else {
    initAuthorName();
  }

  updateCharCount();
  document.body.classList.add('modal-open');
  editModal.classList.add('open');
  editModal.setAttribute('aria-hidden', 'false');
  
  // Start editing point from the TOP (not bottom)
  noteTextarea.scrollTop = 0;
  noteTextarea.setSelectionRange(0, 0);

  const modalBody = editModal.querySelector('.modal-body');
  if (modalBody) modalBody.scrollTop = 0;

  setTimeout(() => {
    noteTextarea.focus({ preventScroll: true });
    noteTextarea.setSelectionRange(0, 0);
    noteTextarea.scrollTop = 0;
    if (modalBody) modalBody.scrollTop = 0;

    if (hasDraft) {
      showToast(`📝 กู้คืนข้อความฉบับร่างของเตียง ${bedNum} ที่พิมพ์ค้างไว้ให้เรียบร้อย`, 'info');
    }
  }, 100);
};

function closeEditModal() {
  if (!templateModal || !templateModal.classList.contains('open')) {
    document.body.classList.remove('modal-open');
  }
  editModal.classList.remove('open');
  editModal.setAttribute('aria-hidden', 'true');
}

function updateCharCount() {
  const text = noteTextarea.value;
  const chars = text.length;
  const lines = text ? text.split('\n').length : 0;
  charCountDisplay.textContent = `${chars} ตัวอักษร | ${lines} บรรทัด`;
}

function insertSnippet(snippet) {
  const curPos = noteTextarea.selectionStart;
  const text = noteTextarea.value;
  const before = text.substring(0, curPos);
  const after = text.substring(curPos);

  const prefix = (before.length > 0 && !before.endsWith('\n')) ? '\n' : '';
  noteTextarea.value = before + prefix + snippet + '\n' + after;
  noteTextarea.focus();
  const nextPos = curPos + prefix.length + snippet.length + 1;
  noteTextarea.setSelectionRange(nextPos, nextPos);
  updateCharCount();
  saveEditDraft();
}

// Save Bed Note to Supabase
async function saveCurrentBed() {
  if (isSaving) return;
  isSaving = true;

  const content = normalizeToCRLF(noteTextarea.value);
  const curUser = getCurrentUser();
  const author = authorInput.value.trim() || (curUser ? (curUser.display_name || curUser.username) : 'มือถือ/เว็บ');
  saveAuthorName(author);

  btnSaveBedNote.disabled = true;
  btnSaveBedNote.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังบันทึก...';

  try {
    const nowUtc = new Date().toISOString();
    const remoteBedNum = getRemoteBedNumber(activeBedNumber);
    
    // 1. Update bed_notes table
    const patchRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${remoteBedNum}`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: content,
        updated_at: nowUtc,
        updated_by: author
      })
    });

    if (!patchRes.ok) throw new Error(`Save failed: ${patchRes.status}`);

    // 2. Insert into bed_history table
    if (content.trim()) {
      fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
        method: 'POST',
        headers: {
          'apikey': SUPABASE_KEY,
          'Authorization': `Bearer ${SUPABASE_KEY}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          bed_number: remoteBedNum,
          reason: 'แก้ไขจากมือถือ/เว็บ',
          content: content,
          char_count: content.length,
          created_at: nowUtc
        })
      }).catch(err => console.warn('History snapshot error:', err));
    }

    // Update local state
    const bed = bedsData.find(b => b.bed_number === activeBedNumber);
    if (bed) {
      bed.content = content;
      bed.updated_at = nowUtc;
      bed.updated_by = author;
      prevContentMap.set(activeBedNumber, content);
    }

    clearEditDraft(activeBedNumber);
    originalEditContent = content;

    showToast(`✅ บันทึกเตียง ${activeBedNumber} ลง Cloud สำเร็จแล้ว!`, 'success');
    closeEditModal();
    renderBeds(new Set([activeBedNumber]));
    updateStats();

  } catch (err) {
    console.error('Save error:', err);
    showToast('❌ บันทึกล้มเหลว กรุณาตรวจสอบการเชื่อมต่ออินเทอร์เน็ต', 'error');
  } finally {
    isSaving = false;
    btnSaveBedNote.disabled = false;
    btnSaveBedNote.innerHTML = '<i class="fa-solid fa-cloud-arrow-up"></i> บันทึกลง Cloud';
  }
}

// Clear Bed Note
async function clearCurrentBed() {
  if (!confirm(`ต้องการล้างข้อมูล เตียง ${activeBedNumber} ใช่หรือไม่?\n(ระบบจะสำรองข้อความเดิมไว้ในประวัติย้อนหลังโดยอัตโนมัติ)`)) {
    return;
  }

  const curBed = bedsData.find(b => b.bed_number === activeBedNumber);
  const oldContent = curBed ? curBed.content : '';

  try {
    const nowUtc = new Date().toISOString();
    const remoteBedNum = getRemoteBedNumber(activeBedNumber);
    const curUser = getCurrentUser();
    const author = authorInput.value.trim() || (curUser ? (curUser.display_name || curUser.username) : 'มือถือ/เว็บ');

    // Archive current note to history first
    if (oldContent && oldContent.trim()) {
      await fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
        method: 'POST',
        headers: {
          'apikey': SUPABASE_KEY,
          'Authorization': `Bearer ${SUPABASE_KEY}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          bed_number: remoteBedNum,
          reason: 'ก่อนล้างเตียง (Clear จากมือถือ/เว็บ)',
          content: oldContent,
          char_count: oldContent.length,
          created_at: nowUtc
        })
      });
    }

    // Clear bed_notes
    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${remoteBedNum}`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: '',
        updated_at: nowUtc,
        updated_by: author
      })
    });

    if (curBed) {
      curBed.content = '';
      curBed.updated_at = nowUtc;
    }

    clearEditDraft(activeBedNumber);
    originalEditContent = '';

    showToast(`ล้างข้อมูลเตียง ${activeBedNumber} เรียบร้อย (สำรองในประวัติแล้ว)`, 'success');
    closeEditModal();
    renderBeds(new Set([activeBedNumber]));
    updateStats();

  } catch (err) {
    showToast('❌ ไม่สามารถล้างเตียงได้ กรุณาลองใหม่อีกครั้ง', 'error');
  }
}

// Copy Bed Content to Clipboard
window.copyBedContent = function(bedNum) {
  const bed = bedsData.find(b => b.bed_number === bedNum);
  if (bed && bed.content) {
    const sel = window.getSelection();
    let textToCopy = normalizeToCRLF(bed.content);
    let isPartial = false;
    if (sel && sel.toString().trim().length > 0) {
      textToCopy = normalizeToCRLF(sel.toString().trim());
      isPartial = true;
    }
    navigator.clipboard.writeText(textToCopy).then(() => {
      showToast(isPartial ? `📋 คัดลอกส่วนที่เลือกเตียง ${bedNum} เรียบร้อย` : `📋 คัดลอกข้อมูลเตียง ${bedNum} แล้ว`, 'success');
    });
  }
};

// ==========================================
// Bed History Viewer Modal
// ==========================================
window.openHistoryModal = async function(bedNum) {
  activeBedNumber = bedNum;
  historyBedBadge.textContent = `เตียง ${String(bedNum).padStart(2, '0')}`;
  historyListPane.innerHTML = '<div class="history-loading"><div class="spinner" style="width:30px;height:30px;"></div>กำลังโหลดประวัติย้อนหลัง...</div>';
  historyPreviewText.value = '';
  historySelectedMeta.textContent = 'เลือกรายการด้านซ้ายเพื่อดูเนื้อหา';
  btnRestoreFromHistory.disabled = true;

  historyModal.classList.add('open');
  historyModal.setAttribute('aria-hidden', 'false');

  try {
    const remoteBedNum = getRemoteBedNumber(bedNum);
    const res = await fetch(`${SUPABASE_URL}/rest/v1/bed_history?bed_number=eq.${remoteBedNum}&order=created_at.desc&limit=50`, {
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });

    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    currentHistoryList = await res.json();

    if (currentHistoryList.length === 0) {
      historyListPane.innerHTML = `
        <div style="padding: 30px 10px; text-align: center; color: var(--text-muted);">
          <i class="fa-regular fa-clock" style="font-size: 30px; margin-bottom: 8px; color: var(--text-dim);"></i>
          <p>ยังไม่มีประวัติย้อนหลังของเตียงนี้</p>
          <small>ระบบจะบันทึกประวัติทันทีเมื่อมีการล้างเตียงหรือแก้ไขข้อมูล</small>
        </div>`;
      return;
    }

    renderHistoryList();

    // Select first item by default
    selectHistoryItem(currentHistoryList[0]);

  } catch (err) {
    historyListPane.innerHTML = '<div class="history-loading" style="color:var(--accent-alert);">ไม่สามารถโหลดประวัติได้</div>';
  }
};

function renderHistoryList() {
  historyListPane.innerHTML = '';
  currentHistoryList.forEach((item, idx) => {
    const div = document.createElement('div');
    div.className = `history-item ${idx === 0 ? 'active' : ''}`;
    div.dataset.id = item.id;

    const timeStr = formatDateTimeThai(item.created_at);
    const snippet = (item.content || '').replace(/\n/g, ' ').substring(0, 45);

    div.innerHTML = `
      <div class="history-item-top">
        <span class="history-item-time"><i class="fa-regular fa-clock"></i> ${timeStr}</span>
        <span class="history-item-reason">${escapeHtml(item.reason || 'บันทึก')}</span>
      </div>
      <div class="history-item-snippet">${escapeHtml(snippet || '(ว่างเปล่า)')}</div>
    `;

    div.addEventListener('click', () => {
      document.querySelectorAll('.history-item').forEach(el => el.classList.remove('active'));
      div.classList.add('active');
      selectHistoryItem(item);
    });

    historyListPane.appendChild(div);
  });
}

function selectHistoryItem(item) {
  selectedHistoryItem = item;
  historyPreviewText.value = item.content || '';
  historySelectedMeta.textContent = `${formatDateTimeThai(item.created_at)} • ${item.reason || 'บันทึก'} (${(item.content || '').length} ตัวอักษร)`;
  btnRestoreFromHistory.disabled = false;
}

async function restoreSelectedHistory() {
  if (!selectedHistoryItem) return;
  if (!confirm(`ต้องการกู้คืนเนื้อหานี้กลับมายัง เตียง ${activeBedNumber} ใช่หรือไม่?`)) return;

  try {
    const nowUtc = new Date().toISOString();
    const content = normalizeToCRLF(selectedHistoryItem.content || '');
    const remoteBedNum = getRemoteBedNumber(activeBedNumber);

    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${remoteBedNum}`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: content,
        updated_at: nowUtc,
        updated_by: `กู้คืนจากประวัติ (${formatDateTimeThai(selectedHistoryItem.created_at)})`
      })
    });

    const bed = bedsData.find(b => b.bed_number === activeBedNumber);
    if (bed) {
      bed.content = content;
      bed.updated_at = nowUtc;
    }

    showToast(`✅ กู้คืนข้อมูลมายังเตียง ${activeBedNumber} สำเร็จแล้ว`, 'success');
    closeHistoryModal();
    renderBeds(new Set([activeBedNumber]));
    updateStats();

  } catch (err) {
    showToast('❌ การกู้คืนล้มเหลว กรุณาลองใหม่', 'error');
  }
}

function closeHistoryModal() {
  historyModal.classList.remove('open');
  historyModal.setAttribute('aria-hidden', 'true');
}

// ==========================================
// Bed Swap & Transfer Modal Logic
// ==========================================
let swapSourceBed = 1;

window.openSwapModal = function(bedNum, defaultTargetBed = null) {
  swapSourceBed = bedNum;
  const sourceBedData = bedsData.find(b => b.bed_number === bedNum) || { content: '' };
  const sourceContent = sourceBedData.content || '';
  const isSourceOccupied = sourceContent.trim().length > 0;

  const badge = document.getElementById('swapSourceBedBadge');
  if (badge) badge.textContent = `เตียง ${String(bedNum).padStart(2, '0')}`;

  const status = document.getElementById('swapSourceStatus');
  if (status) {
    status.textContent = isSourceOccupied ? `มีข้อมูล (${sourceContent.length} ตัวอักษร)` : 'เตียงว่าง';
    status.style.background = isSourceOccupied ? 'var(--primary-light)' : 'var(--border-subtle)';
    status.style.color = isSourceOccupied ? 'var(--primary)' : 'var(--text-dim)';
  }

  const preview = document.getElementById('swapSourcePreview');
  if (preview) {
    preview.textContent = isSourceOccupied 
      ? (sourceContent.slice(0, 180) + (sourceContent.length > 180 ? '...' : '')) 
      : '(เตียงว่าง ไม่มีข้อมูลผู้ป่วย)';
  }

  // Populate target bed dropdown (Bed 1 - 30 except source)
  const select = document.getElementById('swapTargetSelect');
  if (select) {
    select.innerHTML = '';
    let firstSelectIdx = 0;
    let optCount = 0;

    for (let i = 1; i <= 30; i++) {
      if (i === bedNum) continue;
      const bData = bedsData.find(b => b.bed_number === i);
      const bContent = bData && bData.content ? bData.content.trim() : '';
      const opt = document.createElement('option');
      opt.value = i;
      if (bContent) {
        opt.textContent = `เตียง ${String(i).padStart(2, '0')} [มีข้อมูลผู้ป่วย - ${bContent.length} ตัวอักษร]`;
      } else {
        opt.textContent = `เตียง ${String(i).padStart(2, '0')} [ว่าง ✨]`;
      }
      if (defaultTargetBed && i === defaultTargetBed) {
        firstSelectIdx = optCount;
      } else if (!defaultTargetBed && !bContent && firstSelectIdx === 0 && isSourceOccupied) {
        firstSelectIdx = optCount;
      }
      select.appendChild(opt);
      optCount++;
    }

    if (select.options.length > 0) {
      select.selectedIndex = firstSelectIdx;
    }
    handleTargetBedChanged();
  }

  const swapModal = document.getElementById('swapModal');
  if (swapModal) {
    swapModal.classList.add('open');
    swapModal.setAttribute('aria-hidden', 'false');
  }
};

function closeSwapModal() {
  const swapModal = document.getElementById('swapModal');
  if (swapModal) {
    swapModal.classList.remove('open');
    swapModal.setAttribute('aria-hidden', 'true');
  }
}

function handleTargetBedChanged() {
  const select = document.getElementById('swapTargetSelect');
  if (!select) return;
  const targetBedNum = parseInt(select.value, 10);
  const targetData = bedsData.find(b => b.bed_number === targetBedNum);
  const targetContent = targetData && targetData.content ? targetData.content.trim() : '';
  const isTargetOccupied = targetContent.length > 0;

  const rbSwap = document.getElementById('swapModeSwap');
  const rbMove = document.getElementById('swapModeMove');
  const optSwapLabel = document.getElementById('swapOptionSwapLabel');
  const directionText = document.getElementById('swapDirectionText');
  const directionIcon = document.getElementById('swapDirectionIcon');

  if (isTargetOccupied) {
    if (optSwapLabel) optSwapLabel.style.display = 'flex';
    if (rbSwap) {
      rbSwap.disabled = false;
      rbSwap.checked = true;
    }
    if (directionText) directionText.textContent = `สลับข้อมูลกับ เตียง ${String(targetBedNum).padStart(2, '0')}`;
    if (directionIcon) directionIcon.innerHTML = '<i class="fa-solid fa-arrow-right-arrow-left"></i>';
  } else {
    if (optSwapLabel) optSwapLabel.style.display = 'none';
    if (rbMove) {
      rbMove.checked = true;
    }
    if (directionText) directionText.textContent = `ย้ายข้อมูลไปยัง เตียง ${String(targetBedNum).padStart(2, '0')} (เตียงว่าง)`;
    if (directionIcon) directionIcon.innerHTML = '<i class="fa-solid fa-arrow-down"></i>';
  }
}

async function executeBedSwap() {
  const select = document.getElementById('swapTargetSelect');
  if (!select) return;
  const targetBedNum = parseInt(select.value, 10);
  const fromBedNum = swapSourceBed;
  if (!targetBedNum || targetBedNum === fromBedNum) return;

  const rbSwap = document.getElementById('swapModeSwap');
  const isSwap = rbSwap && rbSwap.checked && rbSwap.offsetParent !== null;

  const btnConfirm = document.getElementById('btnConfirmSwap');
  if (btnConfirm) {
    btnConfirm.disabled = true;
    btnConfirm.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังดำเนินการ...';
  }

  try {
    const fromBed = bedsData.find(b => b.bed_number === fromBedNum) || { content: '' };
    const toBed = bedsData.find(b => b.bed_number === targetBedNum) || { content: '' };

    const fromContent = normalizeToCRLF(fromBed.content || '');
    const toContent = normalizeToCRLF(toBed.content || '');
    const curUser = getCurrentUser();
    const author = (authorInput ? authorInput.value.trim() : '') || (curUser ? (curUser.display_name || curUser.username) : 'มือถือ/เว็บ');
    const nowUtc = new Date().toISOString();
    const remoteFromNum = getRemoteBedNumber(fromBedNum);
    const remoteTargetNum = getRemoteBedNumber(targetBedNum);

    // 1. History Snapshots
    const histPromises = [];
    if (fromContent.trim()) {
      histPromises.push(fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
        method: 'POST',
        headers: { 'apikey': SUPABASE_KEY, 'Authorization': `Bearer ${SUPABASE_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bed_number: remoteFromNum,
          reason: isSwap ? `สลับเตียงกับเตียง ${targetBedNum} (จากเว็บ)` : `ย้ายข้อมูลไปยังเตียง ${targetBedNum} (จากเว็บ)`,
          content: fromContent,
          char_count: fromContent.length,
          created_at: nowUtc
        })
      }));
    }
    if (toContent.trim()) {
      histPromises.push(fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
        method: 'POST',
        headers: { 'apikey': SUPABASE_KEY, 'Authorization': `Bearer ${SUPABASE_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bed_number: remoteTargetNum,
          reason: isSwap ? `สลับเตียงกับเตียง ${fromBedNum} (จากเว็บ)` : `รับย้ายข้อมูลมาจากเตียง ${fromBedNum} (สำรองข้อมูลเดิม)`,
          content: toContent,
          char_count: toContent.length,
          created_at: nowUtc
        })
      }));
    }
    await Promise.all(histPromises).catch(err => console.warn('History snapshot warning:', err));

    // 2. Prepare new contents
    const newFromContent = isSwap ? toContent : '';
    const newToContent = fromContent;

    // 3. Update both beds in Supabase bed_notes
    const updatePromises = [
      fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${remoteFromNum}`, {
        method: 'PATCH',
        headers: { 'apikey': SUPABASE_KEY, 'Authorization': `Bearer ${SUPABASE_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: newFromContent, updated_at: nowUtc, updated_by: author })
      }),
      fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${remoteTargetNum}`, {
        method: 'PATCH',
        headers: { 'apikey': SUPABASE_KEY, 'Authorization': `Bearer ${SUPABASE_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: newToContent, updated_at: nowUtc, updated_by: author })
      })
    ];
    await Promise.all(updatePromises);

    // 4. Update local state
    fromBed.content = newFromContent;
    fromBed.updated_at = nowUtc;
    fromBed.updated_by = author;
    prevContentMap.set(fromBedNum, newFromContent);

    toBed.content = newToContent;
    toBed.updated_at = nowUtc;
    toBed.updated_by = author;
    prevContentMap.set(targetBedNum, newToContent);

    // 5. Close modals & update UI
    closeSwapModal();
    closeEditModal();

    showToast(isSwap 
      ? `🔄 สลับข้อมูล เตียง ${fromBedNum} ⮂ เตียง ${targetBedNum} เรียบร้อยแล้ว!` 
      : `➡️ ย้ายข้อมูล เตียง ${fromBedNum} ➜ เตียง ${targetBedNum} สำเร็จ!`, 'success');

    renderBeds(new Set([fromBedNum, targetBedNum]));
    updateStats();

  } catch (err) {
    console.error('Swap error:', err);
    showToast('❌ ไม่สามารถสลับหรือย้ายเตียงได้ กรุณาตรวจสอบการเชื่อมต่อ', 'error');
  } finally {
    if (btnConfirm) {
      btnConfirm.disabled = false;
      btnConfirm.innerHTML = '<i class="fa-solid fa-check"></i> ยืนยันดำเนินการ';
    }
  }
}

// ==========================================
// Helper Utilities
// ==========================================
function formatTimeAgo(dateString) {
  if (!dateString) return 'ไม่มีข้อมูล';
  const diffMs = new Date() - new Date(dateString);
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMins / 60);
  const diffDays = Math.floor(diffHours / 24);

  if (diffMins < 1) return 'เมื่อสักครู่';
  if (diffMins < 60) return `${diffMins} นาทีที่แล้ว`;
  if (diffHours < 24) return `${diffHours} ชม.ที่แล้ว`;
  return `${diffDays} วันที่แล้ว`;
}

function formatDateTimeThai(dateString) {
  if (!dateString) return '';
  const d = new Date(dateString);
  const date = d.toLocaleDateString('th-TH', { day: '2-digit', month: 'short' });
  const time = d.toLocaleTimeString('th-TH', { hour: '2-digit', minute: '2-digit' });
  return `${date} ${time} น.`;
}

function escapeHtml(str) {
  if (!str) return '';
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function showToast(message, type = 'info') {
  const toast = document.createElement('div');
  toast.className = `toast toast-${type}`;
  const icon = type === 'success' ? 'fa-circle-check' : (type === 'error' ? 'fa-triangle-exclamation' : 'fa-info-circle');
  toast.innerHTML = `<i class="fa-solid ${icon}"></i><span>${message}</span>`;
  toastContainer.appendChild(toast);

  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transform = 'translateY(10px)';
    toast.style.transition = 'all 0.3s ease';
    setTimeout(() => toast.remove(), 300);
  }, 3200);
}




/* =====================================================
   Clinical Templates System (DAR & Nursing Guidelines) - Redesigned
   ===================================================== */

let templateCategories = [];
let allTemplates = [];
let activeTemplateCat = 'all';
let activeTemplateSearch = '';
let selectedTemplate = null;
let templateModalTriggerSource = 'navbar';
let currentPreviewViewMode = 'dar'; // 'dar' or 'raw'

// Template Modal DOM Elements
const openTemplateLibraryBtn = document.getElementById('openTemplateLibraryBtn');
const btnOpenTemplatePicker = document.getElementById('btnOpenTemplatePicker');
const templateModal = document.getElementById('templateModal');
const templateModalCloseBtn = document.getElementById('templateModalCloseBtn');
const btnCloseTemplateModal = document.getElementById('btnCloseTemplateModal');
const templateSearchInput = document.getElementById('templateSearchInput');
const clearTemplateSearchBtn = document.getElementById('clearTemplateSearchBtn');
const templateCategoryPills = document.getElementById('templateCategoryPills');
const templateContentSplit = document.getElementById('templateContentSplit');
const templateListPane = document.getElementById('templateListPane');
const templatePreviewPane = document.getElementById('templatePreviewPane');
const templatePreviewEmpty = document.getElementById('templatePreviewEmpty');
const templatePreviewContent = document.getElementById('templatePreviewContent');
const btnMobileBackToList = document.getElementById('btnMobileBackToList');
const previewShortcutTag = document.getElementById('previewShortcutTag');
const previewTitle = document.getElementById('previewTitle');
const previewCategoryTag = document.getElementById('previewCategoryTag');
const previewFormattedView = document.getElementById('previewFormattedView');
const previewRawView = document.getElementById('previewRawView');
const templateRawText = document.getElementById('templateRawText');
const tabViewDAR = document.getElementById('tabViewDAR');
const tabViewRaw = document.getElementById('tabViewRaw');
const btnCopyTemplate = document.getElementById('btnCopyTemplate');
const btnInsertTemplate = document.getElementById('btnInsertTemplate');
const btnApplyTemplate = document.getElementById('btnApplyTemplate');
const btnInsertText = document.getElementById('btnInsertText');
const btnApplyText = document.getElementById('btnApplyText');
const bedTargetSelectWrapper = document.getElementById('bedTargetSelectWrapper');
const targetBedSelect = document.getElementById('targetBedSelect');
const templateCountBadge = document.getElementById('templateCountBadge');
const previewShiftBar = document.getElementById('previewShiftBar');
const tabShiftMorning = document.getElementById('tabShiftMorning');
const tabShiftAfternoon = document.getElementById('tabShiftAfternoon');
const tabShiftNight = document.getElementById('tabShiftNight');
const tabShiftAll = document.getElementById('tabShiftAll');
let currentTemplateShift = 'morning';

function hasShiftTags(content) {
  if (!content) return false;
  return content.includes('เวรเช้า') || content.includes('เวรบ่าย') || content.includes('เวรดึก');
}

function getCurrentShift() {
  const hour = new Date().getHours();
  if (hour >= 8 && hour < 16) return 'morning';
  if (hour >= 16 && hour < 24) return 'afternoon';
  return 'night';
}

function filterContentByShift(content, shift) {
  if (!content || shift === 'all' || !hasShiftTags(content)) {
    return content;
  }

  const lines = content.split(/\r?\n/);
  const result = [];
  let inAction = false;
  let curActionShift = '';
  const filteredActions = [];
  const nonShiftActions = [];

  let shiftTag = '';
  if (shift === 'morning') shiftTag = ' (☀️ เวรเช้า 08:00-16:00)';
  else if (shift === 'afternoon') shiftTag = ' (⛅ เวรบ่าย 16:00-24:00)';
  else if (shift === 'night') shiftTag = ' (🌙 เวรดึก 24:00-08:00)';

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    if (trimmed.startsWith('Focus:') || trimmed.startsWith('Goal:') || trimmed.startsWith('Data:') || trimmed.startsWith('Response:')) {
      if (inAction) {
        result.push('Action:' + shiftTag);
        if (filteredActions.length > 0) {
          result.push(...filteredActions);
        } else if (nonShiftActions.length > 0) {
          result.push(...nonShiftActions);
        }
        inAction = false;
        curActionShift = '';
      }
      result.push(line);
      continue;
    }

    if (trimmed.startsWith('Action:')) {
      inAction = true;
      curActionShift = '';
      filteredActions.length = 0;
      nonShiftActions.length = 0;
      continue;
    }

    if (inAction) {
      if (trimmed.includes('เวรเช้า')) {
        curActionShift = 'morning';
        continue;
      } else if (trimmed.includes('เวรบ่าย')) {
        curActionShift = 'afternoon';
        continue;
      } else if (trimmed.includes('เวรดึก')) {
        curActionShift = 'night';
        continue;
      }

      if (curActionShift) {
        if (curActionShift === shift) {
          filteredActions.push(line);
        }
      } else {
        nonShiftActions.push(line);
      }
      continue;
    }

    result.push(line);
  }

  if (inAction) {
    result.push('Action:' + shiftTag);
    if (filteredActions.length > 0) {
      result.push(...filteredActions);
    } else if (nonShiftActions.length > 0) {
      result.push(...nonShiftActions);
    }
  }

  return normalizeToCRLF(result.join('\r\n'));
}

function getEffectiveTemplateContent(item) {
  if (!item || !item.content) return '';
  if (hasShiftTags(item.content) && currentTemplateShift !== 'all') {
    return filterContentByShift(item.content, currentTemplateShift);
  }
  return item.content;
}

function updateShiftButtonsActiveState() {
  const shiftBtns = [tabShiftMorning, tabShiftAfternoon, tabShiftNight, tabShiftAll];
  shiftBtns.forEach(btn => {
    if (btn) {
      btn.classList.toggle('active', btn.dataset.shift === currentTemplateShift);
    }
  });
}

function refreshTemplatePreview() {
  if (!selectedTemplate) return;
  const effective = getEffectiveTemplateContent(selectedTemplate);
  if (templateRawText) templateRawText.value = effective;
  if (previewFormattedView) previewFormattedView.innerHTML = formatDARHtml(effective);
}

// Fetch and initialize templates from Supabase Cloud (Row 102) with fallback to templates.json
async function initClinicalTemplates() {
  let loaded = false;

  // 1. Try Supabase Cloud Row 102 first (Realtime Cloud Sync)
  try {
    const cloudRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.102&select=content,updated_at,updated_by`, {
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });
    if (cloudRes.ok) {
      const data = await cloudRes.json();
      if (data && data.length > 0 && data[0].content) {
        let contentRaw = data[0].content;
        let parsed = null;
        if (typeof contentRaw === 'string') {
          try { parsed = JSON.parse(contentRaw); } catch (e) { console.warn('Cloud row 102 JSON parse failed', e); }
        } else {
          parsed = contentRaw;
        }

        if (parsed) {
          if (Array.isArray(parsed.categories) && parsed.categories.length > 0) {
            templateCategories = parsed.categories;
            loaded = true;
          } else if (Array.isArray(parsed) && parsed.length > 0) {
            templateCategories = parsed;
            loaded = true;
          }
        }
        if (loaded) {
          console.log(`[Templates] Loaded ${templateCategories.length} categories directly from Supabase Cloud (Row 102)`);
        }
      }
    }
  } catch (cloudErr) {
    console.warn('[Templates] Could not fetch from Supabase Cloud Row 102, falling back to static templates.json:', cloudErr);
  }

  // 2. Fallback to templates.json if cloud fetch was not possible
  if (!loaded) {
    try {
      const res = await fetch('templates.json?v=1.9.5');
      if (!res.ok) throw new Error('Cannot load templates.json');
      templateCategories = await res.json();
      loaded = true;
      console.log(`[Templates] Loaded ${templateCategories.length} categories from templates.json fallback`);
    } catch (err) {
      console.warn('Failed to fetch templates.json', err);
    }
  }

  if (loaded && templateCategories) {
    // Flatten templates into searchable array
    allTemplates = [];
    templateCategories.forEach((cat, catIdx) => {
      if (cat.items && Array.isArray(cat.items)) {
        cat.items.forEach(item => {
          allTemplates.push({
            shortcut: item.shortcut,
            title: item.title,
            content: item.content,
            category: cat.category,
            catIndex: catIdx
          });
        });
      }
    });

    renderTemplateCategoryPills();
    renderTemplatesList();
    if (allTemplates.length > 0) {
      selectTemplate(allTemplates[0], false);
    }
  }

  setupTemplateEventListeners();
}

// Setup Event Listeners for Templates
function setupTemplateEventListeners() {
  if (openTemplateLibraryBtn) {
    openTemplateLibraryBtn.addEventListener('click', () => {
      if (!getCurrentUser()) {
        openLoginModal(true, 'กรุณาเข้าสู่ระบบก่อนเข้าใช้งานคลังเทมเพลต');
        return;
      }
      resetIdleTimer();
      openTemplateModal('navbar');
    });
  }
  if (btnOpenTemplatePicker) {
    btnOpenTemplatePicker.addEventListener('click', () => openTemplateModal('editModal'));
  }
  if (templateModalCloseBtn) {
    templateModalCloseBtn.addEventListener('click', closeTemplateModal);
  }
  if (btnCloseTemplateModal) {
    btnCloseTemplateModal.addEventListener('click', closeTemplateModal);
  }
  if (templateModal) {
    templateModal.addEventListener('click', (e) => {
      if (e.target === templateModal) closeTemplateModal();
    });
  }

  // Mobile Back Button
  if (btnMobileBackToList) {
    btnMobileBackToList.addEventListener('click', () => {
      if (templateContentSplit) {
        templateContentSplit.classList.remove('detail-active');
      }
    });
  }

  // View Switcher Tabs (DAR vs Raw)
  if (tabViewDAR) {
    tabViewDAR.addEventListener('click', () => switchPreviewView('dar'));
  }
  if (tabViewRaw) {
    tabViewRaw.addEventListener('click', () => switchPreviewView('raw'));
  }

  // Search input
  if (templateSearchInput) {
    templateSearchInput.addEventListener('input', (e) => {
      activeTemplateSearch = e.target.value.trim().toLowerCase();
      renderTemplatesList();
    });
  }

  if (clearTemplateSearchBtn) {
    clearTemplateSearchBtn.addEventListener('click', () => {
      if (templateSearchInput) {
        templateSearchInput.value = '';
        activeTemplateSearch = '';
        renderTemplatesList();
        templateSearchInput.focus();
      }
    });
  }

  if (btnCopyTemplate) {
    btnCopyTemplate.addEventListener('click', copySelectedTemplate);
  }
  if (btnInsertTemplate) {
    btnInsertTemplate.addEventListener('click', () => applyTemplateToNote(false));
  }
  if (btnApplyTemplate) {
    btnApplyTemplate.addEventListener('click', () => applyTemplateToNote(true));
  }

  const shiftBtns = [tabShiftMorning, tabShiftAfternoon, tabShiftNight, tabShiftAll];
  shiftBtns.forEach(btn => {
    if (btn) {
      btn.addEventListener('click', (e) => {
        currentTemplateShift = e.currentTarget.dataset.shift;
        updateShiftButtonsActiveState();
        refreshTemplatePreview();
      });
    }
  });
}

// Switch between DAR formatted and raw text view
function switchPreviewView(mode) {
  currentPreviewViewMode = mode;
  if (tabViewDAR && tabViewRaw && previewFormattedView && previewRawView) {
    if (mode === 'dar') {
      tabViewDAR.classList.add('active');
      tabViewRaw.classList.remove('active');
      previewFormattedView.style.display = 'flex';
      previewRawView.style.display = 'none';
    } else {
      tabViewDAR.classList.remove('active');
      tabViewRaw.classList.add('active');
      previewFormattedView.style.display = 'none';
      previewRawView.style.display = 'flex';
    }
  }
}

// Open Template Modal
function openTemplateModal(source = 'navbar') {
  templateModalTriggerSource = source;

  // Reset mobile view to list
  if (templateContentSplit) {
    templateContentSplit.classList.remove('detail-active');
  }

  // Setup Bed Target Selector
  if (targetBedSelect) {
    targetBedSelect.innerHTML = '';
    for (let b = 1; b <= 30; b++) {
      const opt = document.createElement('option');
      opt.value = b;
      opt.textContent = `เตียง ${String(b).padStart(2, '0')}`;
      if (b === activeBedNumber) opt.selected = true;
      targetBedSelect.appendChild(opt);
    }
  }

  if (source === 'editModal') {
    if (bedTargetSelectWrapper) bedTargetSelectWrapper.style.display = 'none';
    if (btnInsertText) btnInsertText.textContent = 'แทรกในบันทึกเตียงนี้';
    if (btnApplyText) btnApplyText.textContent = 'แทนที่ทั้งหมดในเตียงนี้';
  } else {
    if (bedTargetSelectWrapper) bedTargetSelectWrapper.style.display = 'flex';
    if (btnInsertText) btnInsertText.textContent = 'แทรกต่อท้าย';
    if (btnApplyText) btnApplyText.textContent = 'ใช้ที่เตียงที่เลือก';
  }

  if (templateModal) {
    document.body.classList.add('modal-open');
    templateModal.classList.add('open');
    templateModal.setAttribute('aria-hidden', 'false');
  }

  // Pre-select first or current
  renderTemplatesList();
  if (selectedTemplate) {
    selectTemplate(selectedTemplate, false);
  } else if (allTemplates.length > 0) {
    selectTemplate(allTemplates[0], false);
  }

  switchPreviewView('dar');

  setTimeout(() => {
    if (templateSearchInput) templateSearchInput.focus();
  }, 100);
}

// Close Template Modal
function closeTemplateModal() {
  if (!editModal || !editModal.classList.contains('open')) {
    document.body.classList.remove('modal-open');
  }
  if (templateModal) {
    templateModal.classList.remove('open');
    templateModal.setAttribute('aria-hidden', 'true');
  }
  if (templateContentSplit) {
    templateContentSplit.classList.remove('detail-active');
  }
}

// Category clean names mapping (avoiding awkward ".." truncation)
const friendlyCategoryNames = [
  { icon: "fa-procedures", label: "ก่อน-หลังผ่าตัด" },
  { icon: "fa-fire", label: "จัดการความปวด" },
  { icon: "fa-bone", label: "ตรวจระบบกระดูก" },
  { icon: "fa-heart-pulse", label: "อายุรกรรม" },
  { icon: "fa-shield-halved", label: "ป้องกันแทรกซ้อน" },
  { icon: "fa-droplet", label: "สารน้ำ & ขับถ่าย" },
  { icon: "fa-clipboard-user", label: "ส่งเวร & จำหน่าย" },
  { icon: "fa-bolt", label: "คีย์ลัดย่อด่วน" },
  { icon: "fa-person-walking-with-cane", label: "ข้อเข่าเสื่อม (TKA/UKA)" },
  { icon: "fa-wheelchair", label: "ข้อสะโพก (THA/BHA)" },
  { icon: "fa-dna", label: "กระดูกสันหลัง (Spine)" },
  { icon: "fa-bandage", label: "กระดูกหัก (ORIF/Cast)" },
  { icon: "fa-hospital", label: "ศัลยกรรมเฉพาะทาง" },
  { icon: "fa-vial-circle-check", label: "เกลือแร่ (Electrolytes)" },
  { icon: "fa-droplet-slash", label: "ค่าเลือดผิดปกติ (Critical Labs)" },
  { icon: "fa-dove", label: "ระยะสุดท้าย (Palliative)" },
  { icon: "fa-brain", label: "ระบบประสาท (Neuro)" },
  { icon: "fa-lungs", label: "เครื่องช่วยหายใจ (Ventilator)" },
  { icon: "fa-kit-medical", label: "วิกฤต & ช่วยชีวิต (Critical Care)" },
  { icon: "fa-radiation", label: "มะเร็งกระดูก & ฉายแสง (Bone Ca & RT)" }
];

// Render Category Pills with Mouse-Wheel Horizontal Scroll
function renderTemplateCategoryPills() {
  if (!templateCategoryPills) return;
  templateCategoryPills.innerHTML = '';
  
  if (!templateCategoryPills._hasWheelListener) {
    templateCategoryPills.addEventListener('wheel', (e) => {
      if (e.deltaY !== 0) {
        e.preventDefault();
        templateCategoryPills.scrollLeft += (e.deltaY * 1.5);
      }
    }, { passive: false });
    templateCategoryPills._hasWheelListener = true;
  }

  // All pill
  const allPill = document.createElement('button');
  allPill.type = 'button';
  allPill.className = `cat-pill ${activeTemplateCat === 'all' ? 'active' : ''}`;
  allPill.innerHTML = `<i class="fa-solid fa-layer-group"></i> ทั้งหมด (${allTemplates.length})`;
  allPill.addEventListener('click', () => {
    activeTemplateCat = 'all';
    updateCategoryPillsActiveState();
    renderTemplatesList();
  });
  templateCategoryPills.appendChild(allPill);

  templateCategories.forEach((cat, idx) => {
    const pill = document.createElement('button');
    pill.type = 'button';
    pill.className = `cat-pill ${activeTemplateCat === String(idx) ? 'active' : ''}`;
    
    const friendly = friendlyCategoryNames[idx] || { icon: "fa-briefcase-medical", label: cat.category.split('(')[0].replace(/^\d+\.\s*/, '').trim() };

    pill.innerHTML = `<i class="fa-solid ${friendly.icon}"></i> ${friendly.label} (${cat.items.length})`;
    pill.addEventListener('click', () => {
      activeTemplateCat = String(idx);
      updateCategoryPillsActiveState();
      renderTemplatesList();
    });
    templateCategoryPills.appendChild(pill);
  });
}

function updateCategoryPillsActiveState() {
  if (!templateCategoryPills) return;
  const pills = templateCategoryPills.querySelectorAll('.cat-pill');
  pills.forEach((p, idx) => {
    if (idx === 0) {
      p.classList.toggle('active', activeTemplateCat === 'all');
    } else {
      p.classList.toggle('active', activeTemplateCat === String(idx - 1));
    }
  });
}

// Render Templates List (Filtered by search & category)
function renderTemplatesList() {
  if (!templateListPane) return;
  templateListPane.innerHTML = '';

  const filtered = allTemplates.filter(item => {
    // Category match
    if (activeTemplateCat !== 'all' && String(item.catIndex) !== activeTemplateCat) {
      return false;
    }
    // Search match (shortcut, title, content, category)
    if (activeTemplateSearch) {
      const q = activeTemplateSearch;
      const match = item.shortcut.toLowerCase().includes(q) ||
                    item.title.toLowerCase().includes(q) ||
                    item.category.toLowerCase().includes(q) ||
                    item.content.toLowerCase().includes(q);
      if (!match) return false;
    }
    return true;
  });

  if (templateCountBadge) {
    templateCountBadge.textContent = `${filtered.length} เทมเพลต`;
  }

  if (filtered.length === 0) {
    templateListPane.innerHTML = `
      <div style="text-align: center; padding: 40px 20px; color: var(--text-muted);">
        <i class="fa-solid fa-magnifying-glass" style="font-size: 2rem; margin-bottom: 10px; opacity: 0.4;"></i>
        <p style="font-size: 0.92rem; margin: 0;">ไม่พบข้อวินิจฉัยที่ตรงกับ "${escapeHtml(activeTemplateSearch)}"</p>
      </div>
    `;
    return;
  }

  filtered.forEach(item => {
    const card = document.createElement('div');
    card.className = `template-card ${selectedTemplate && selectedTemplate.shortcut === item.shortcut ? 'active' : ''}`;
    
    // Extract Focus line for card snippet
    let focusSnippet = '';
    const lines = item.content.split('\n');
    for (const l of lines) {
      if (l.trim().startsWith('Focus:')) {
        focusSnippet = l.replace('Focus:', '').trim();
        break;
      }
    }
    if (!focusSnippet && lines.length > 0) {
      focusSnippet = lines[0].substring(0, 70);
    }

    // Clean Category label
    const friendly = friendlyCategoryNames[item.catIndex];
    const catLabel = friendly ? friendly.label.split('(')[0].trim() : item.category.replace(/^\d+\.\s*/, '').split('(')[0].trim();

    card.innerHTML = `
      <div class="template-card-header">
        <span class="template-card-shortcut">${escapeHtml(item.shortcut)}</span>
        <span class="template-card-cat-label">${escapeHtml(catLabel)}</span>
      </div>
      <div class="template-card-title">${escapeHtml(item.title)}</div>
      <div class="template-card-snippet">${escapeHtml(focusSnippet)}</div>
    `;

    card.addEventListener('click', () => {
      selectTemplate(item, true); // true = activate mobile detail
      const allCards = templateListPane.querySelectorAll('.template-card');
      allCards.forEach(c => c.classList.remove('active'));
      card.classList.add('active');
    });

    templateListPane.appendChild(card);
  });
}

// Select a Template and Display DAR View
function selectTemplate(item, activateMobileDetail = false) {
  selectedTemplate = item;
  if (!templatePreviewContent || !templatePreviewEmpty) return;

  templatePreviewEmpty.style.display = 'none';
  templatePreviewContent.style.display = 'flex';

  if (previewShortcutTag) previewShortcutTag.textContent = item.shortcut;
  if (previewTitle) previewTitle.textContent = item.title;
  
  const friendly = friendlyCategoryNames[item.catIndex];
  const catName = friendly ? friendly.label : item.category.replace(/^\d+\.\s*/, '');
  if (previewCategoryTag) previewCategoryTag.textContent = catName;

  if (hasShiftTags(item.content)) {
    if (previewShiftBar) previewShiftBar.style.display = 'flex';
    currentTemplateShift = getCurrentShift();
  } else {
    if (previewShiftBar) previewShiftBar.style.display = 'none';
    currentTemplateShift = 'all';
  }
  updateShiftButtonsActiveState();

  const effective = getEffectiveTemplateContent(item);
  if (templateRawText) templateRawText.value = effective;

  // Format DAR
  if (previewFormattedView) {
    previewFormattedView.innerHTML = formatDARHtml(effective);
  }

  // Mobile activation
  if (activateMobileDetail && templateContentSplit) {
    templateContentSplit.classList.add('detail-active');
  }
}

// Parse raw template text into styled DAR sections
function formatDARHtml(content) {
  if (!content) return '';
  
  const lines = content.split('\n');
  let currentSection = 'general';
  const sections = {
    focus: [],
    goal: [],
    data: [],
    action: [],
    response: [],
    general: []
  };

  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed.startsWith('Focus:')) {
      currentSection = 'focus';
      sections.focus.push(trimmed.replace('Focus:', '').trim());
    } else if (trimmed.startsWith('Goal:')) {
      currentSection = 'goal';
      const gVal = trimmed.replace('Goal:', '').trim();
      if (gVal) sections.goal.push(gVal);
    } else if (trimmed.startsWith('Data:')) {
      currentSection = 'data';
      sections.data.push(trimmed.replace('Data:', '').trim());
    } else if (trimmed.startsWith('Action:')) {
      currentSection = 'action';
      const aVal = trimmed.replace('Action:', '').trim();
      if (aVal) sections.action.push(aVal);
    } else if (trimmed.startsWith('Response:')) {
      currentSection = 'response';
      const rVal = trimmed.replace('Response:', '').trim();
      if (rVal) sections.response.push(rVal);
    } else {
      if (currentSection === 'focus') sections.focus.push(line);
      else if (currentSection === 'goal') sections.goal.push(line);
      else if (currentSection === 'data') sections.data.push(line);
      else if (currentSection === 'action') sections.action.push(line);
      else if (currentSection === 'response') sections.response.push(line);
      else sections.general.push(line);
    }
  }

  // If not standard DAR (e.g., .vs, .order, shortcuts), show general clean text
  if (sections.focus.length === 0 && sections.goal.length === 0 && sections.data.length === 0 && sections.action.length === 0) {
    return `
      <div class="dar-section">
        <span class="dar-tag" style="background: rgba(13, 148, 136, 0.15); color: var(--primary);">เนื้อหาข้อความ</span>
        <div class="dar-text" style="font-family: var(--font-mono); font-size: 1rem; line-height: 1.8;">${escapeHtml(content)}</div>
      </div>
    `;
  }

  let html = '';
  if (sections.focus.length > 0) {
    html += `
      <div class="dar-section focus-sec">
        <span class="dar-tag"><i class="fa-solid fa-bullseye"></i> Focus (ข้อวินิจฉัย/ปัญหาทางการพยาบาล)</span>
        <div class="dar-text">${escapeHtml(sections.focus.join('\n').trim())}</div>
      </div>
    `;
  }
  if (sections.goal.length > 0) {
    html += `
      <div class="dar-section goal-sec">
        <span class="dar-tag"><i class="fa-solid fa-flag-checkered"></i> Goal (เป้าหมายทางการพยาบาลและผลลัพธ์ที่คาดหวัง)</span>
        <div class="dar-text">${escapeHtml(sections.goal.join('\n').trim())}</div>
      </div>
    `;
  }
  if (sections.data.length > 0) {
    html += `
      <div class="dar-section data-sec">
        <span class="dar-tag"><i class="fa-solid fa-clipboard-check"></i> Data (ข้อมูลอาการและผลตรวจ S & O)</span>
        <div class="dar-text">${escapeHtml(sections.data.join('\n').trim())}</div>
      </div>
    `;
  }
  if (sections.action.length > 0) {
    // Format action items nicely
    const actionText = escapeHtml(sections.action.join('\n').trim())
      .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>'); // bold markdown support

    html += `
      <div class="dar-section action-sec">
        <span class="dar-tag"><i class="fa-solid fa-user-nurse"></i> Action (กิจกรรมการพยาบาล & ข้อควรระวัง)</span>
        <div class="dar-text">${actionText}</div>
      </div>
    `;
  }
  if (sections.response.length > 0) {
    html += `
      <div class="dar-section response-sec">
        <span class="dar-tag"><i class="fa-solid fa-square-check"></i> Response (การประเมินผลลัพธ์)</span>
        <div class="dar-text">${escapeHtml(sections.response.join('\n').trim())}</div>
      </div>
    `;
  }

  return html;
}

// Copy Selected Template to Clipboard (Supports partial text selection)
function copySelectedTemplate() {
  if (!selectedTemplate) return;
  
  let textToCopy = '';
  const sel = window.getSelection();
  if (sel && sel.toString().trim().length > 0) {
    textToCopy = sel.toString().trim();
  } else {
    const rawTa = document.getElementById('templateRawText');
    if (rawTa && rawTa.offsetParent !== null && rawTa.selectionStart !== rawTa.selectionEnd) {
      textToCopy = rawTa.value.substring(rawTa.selectionStart, rawTa.selectionEnd).trim();
    }
  }

  const isPartial = textToCopy.length > 0;
  if (!isPartial) {
    const rawTa = document.getElementById('templateRawText');
    if (rawTa && currentPreviewViewMode === 'raw' && rawTa.value.trim().length > 0) {
      textToCopy = rawTa.value;
    } else {
      textToCopy = getEffectiveTemplateContent(selectedTemplate);
    }
  }
  textToCopy = normalizeToCRLF(textToCopy);

  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(textToCopy).then(() => {
      showToast(isPartial ? `📋 คัดลอกส่วนที่เลือก (${textToCopy.length} ตัวอักษร) เรียบร้อย` : `📋 คัดลอกข้อวินิจฉัย [${selectedTemplate.shortcut}] เรียบร้อย`, 'success');
    }).catch(() => {
      fallbackCopy(textToCopy, isPartial);
    });
  } else {
    fallbackCopy(textToCopy, isPartial);
  }
}

function fallbackCopy(text, isPartial = false) {
  const ta = document.createElement('textarea');
  ta.value = normalizeToCRLF(text);
  document.body.appendChild(ta);
  ta.select();
  document.execCommand('copy');
  document.body.removeChild(ta);
  showToast(isPartial ? `📋 คัดลอกส่วนที่เลือก (${text.length} ตัวอักษร) เรียบร้อย` : `📋 คัดลอก [${selectedTemplate ? selectedTemplate.shortcut : ''}] เรียบร้อย`, 'success');
}

// Apply Template to Note (Insert at cursor / Append / Replace)
function applyTemplateToNote(replace = false) {
  if (!selectedTemplate) return;

  let contentToApply = '';
  const sel = window.getSelection();
  if (sel && sel.toString().trim().length > 0) {
    contentToApply = sel.toString().trim();
  } else {
    const rawTa = document.getElementById('templateRawText');
    if (rawTa && rawTa.offsetParent !== null && rawTa.selectionStart !== rawTa.selectionEnd) {
      const sub = rawTa.value.substring(rawTa.selectionStart, rawTa.selectionEnd).trim();
      if (sub.length > 0) contentToApply = sub;
    }
  }

  if (!contentToApply) {
    const rawTa = document.getElementById('templateRawText');
    if (rawTa && currentPreviewViewMode === 'raw' && rawTa.value.trim().length > 0) {
      contentToApply = rawTa.value;
    } else {
      contentToApply = getEffectiveTemplateContent(selectedTemplate);
    }
  }

  if (templateModalTriggerSource === 'editModal') {
    // Inside Edit Bed Note modal
    if (replace) {
      noteTextarea.value = contentToApply;
      noteTextarea.scrollTop = 0;
      noteTextarea.setSelectionRange(0, 0);
    } else {
      insertSnippet(contentToApply);
    }
    updateCharCount();
    saveEditDraft();
    closeTemplateModal();
    showToast(`✨ ${replace ? 'แทนที่ข้อความ' : 'แทรกข้อวินิจฉัย'} [${selectedTemplate.shortcut}] เรียบร้อย`, 'success');
    if (replace) {
      setTimeout(() => {
        noteTextarea.focus({ preventScroll: true });
        noteTextarea.setSelectionRange(0, 0);
        noteTextarea.scrollTop = 0;
      }, 50);
    } else {
      noteTextarea.focus();
    }
  } else {
    // From Navbar: Target a specific bed
    const targetBed = targetBedSelect ? parseInt(targetBedSelect.value, 10) : activeBedNumber;
    closeTemplateModal();
    
    // Open Edit Modal for target bed
    window.openEditModal(targetBed);
    
    setTimeout(() => {
      if (replace) {
        noteTextarea.value = contentToApply;
        noteTextarea.scrollTop = 0;
        noteTextarea.setSelectionRange(0, 0);
      } else {
        const curText = noteTextarea.value.trim();
        noteTextarea.value = curText ? `${curText}\n\n${contentToApply}` : contentToApply;
      }
      updateCharCount();
      saveEditDraft();
      showToast(`✨ นำข้อวินิจฉัย [${selectedTemplate.shortcut}] ใส่เตียง ${String(targetBed).padStart(2, '0')} เรียบร้อย`, 'success');
      noteTextarea.focus({ preventScroll: true });
      if (replace) {
        noteTextarea.setSelectionRange(0, 0);
        noteTextarea.scrollTop = 0;
      }
    }, 150);
  }
}

// ==========================================
// ============================================================
// Ward Documents & Forms Center System (Multi-Document Catalog)
// ============================================================
const DEFAULT_IO_DOC = {
  id: 'doc_io_template',
  title: 'แบบฟอร์มบันทึก Intake / Output (I/O)',
  category: 'แบบฟอร์มบันทึกทางการพยาบาล',
  filename: 'แบบฟอร์ม IO.xlsx',
  download_name: 'แบบฟอร์ม_บันทึก_IO.xlsx',
  file_type: 'xlsx',
  size: 78780,
  updated_at: '2026-10-03T01:58:06Z',
  updated_by: 'Admin',
  static_url: 'templates/IO_Template.xlsx'
};

let currentDocsCatalog = {
  version: 2,
  documents: [ DEFAULT_IO_DOC ]
};

let selectedDocFile = null;
let isDocsAdminUnlocked = false;
let activeDocCategory = 'all';
let docSearchQuery = '';

// Backward compatibility references
let currentIoTemplate = DEFAULT_IO_DOC;

function bumpVersionString(currentVer) {
  if (!currentVer) return 'v1.0';
  const str = String(currentVer).trim();
  const match = str.match(/^(v?)(\d+)(?:\.(\d+))?$/i);
  if (match) {
    const prefix = match[1] || 'v';
    const major = parseInt(match[2], 10);
    const minor = match[3] !== undefined ? parseInt(match[3], 10) : 0;
    return `${prefix}${major}.${minor + 1}`;
  }
  return str.endsWith('.0') ? str.replace(/\.0$/, '.1') : (str + '.1');
}

function extractOrGenerateVersion(fileName, existingDoc) {
  if (existingDoc && (existingDoc.doc_version || existingDoc.version)) {
    return bumpVersionString(existingDoc.doc_version || existingDoc.version);
  }
  if (fileName) {
    const m = fileName.match(/[_\-\s]v?(\d+\.\d+)/i);
    if (m) return 'v' + m[1];
  }
  return 'v1.0';
}

function getNextDocOrder(catalogDocs) {
  if (!Array.isArray(catalogDocs) || catalogDocs.length === 0) return 1;
  let maxOrder = 0;
  catalogDocs.forEach((d, idx) => {
    let num = parseInt(d.order || d.seq, 10);
    if (isNaN(num) || num <= 0) {
      const m = (d.title || d.filename || '').match(/^(\d+)[\.\-_]/);
      if (m) num = parseInt(m[1], 10);
      else num = idx + 1;
      d.order = num;
      d.seq = num;
    }
    if (num > maxOrder) maxOrder = num;
  });
  return maxOrder + 1;
}

function normalizeDocsCatalog(raw) {
  let catalog;
  if (!raw) {
    catalog = { version: 2, documents: [ DEFAULT_IO_DOC ] };
  } else if (Array.isArray(raw.documents)) {
    catalog = { version: raw.version || 2, documents: raw.documents.length === 0 ? [ DEFAULT_IO_DOC ] : raw.documents };
  } else if (raw.filename || raw.base64) {
    catalog = {
      version: 2,
      documents: [
        {
          id: 'doc_io_template',
          order: 1,
          seq: 1,
          doc_version: 'v1.0',
          version: 'v1.0',
          title: 'แบบฟอร์มบันทึก Intake / Output (I/O)',
          category: 'แบบฟอร์มบันทึกทางการพยาบาล',
          filename: raw.filename || 'แบบฟอร์ม IO.xlsx',
          download_name: raw.download_name || 'แบบฟอร์ม_บันทึก_IO.xlsx',
          file_type: 'xlsx',
          size: raw.size || 78780,
          updated_at: raw.updated_at || new Date().toISOString(),
          updated_by: raw.updated_by || 'Admin',
          base64: raw.base64
        }
      ]
    };
  } else {
    catalog = { version: 2, documents: [ DEFAULT_IO_DOC ] };
  }

  // Ensure every document has order and doc_version
  catalog.documents.forEach((doc, idx) => {
    if (doc.order === undefined || doc.order === null || isNaN(parseInt(doc.order, 10))) {
      let ord = parseInt(doc.seq, 10);
      if (isNaN(ord) || ord <= 0) {
        const m = (doc.title || doc.filename || '').match(/^(\d+)[\.\-_]/);
        if (m) ord = parseInt(m[1], 10);
        else ord = idx + 1;
      }
      doc.order = ord;
      doc.seq = ord;
    } else {
      doc.order = parseInt(doc.order, 10);
      doc.seq = doc.order;
    }

    if (!doc.doc_version && !doc.version) {
      doc.doc_version = 'v1.0';
      doc.version = 'v1.0';
    } else if (!doc.doc_version && doc.version) {
      doc.doc_version = doc.version;
    } else if (doc.doc_version && !doc.version) {
      doc.version = doc.doc_version;
    }
  });

  // Sort by order ascending
  catalog.documents.sort((a, b) => {
    const diff = (a.order || 0) - (b.order || 0);
    return diff !== 0 ? diff : (a.title || '').localeCompare(b.title || '');
  });

  return catalog;
}

function initDocumentsSystem() {
  const openDocsModalBtn = document.getElementById('openDocsModalBtn') || document.getElementById('openIoModalBtn');
  const docsModal = document.getElementById('docsModal') || document.getElementById('ioModal');
  const docsModalCloseBtn = document.getElementById('docsModalCloseBtn') || document.getElementById('ioModalCloseBtn');
  const btnCloseDocsModal = document.getElementById('btnCloseDocsModal') || document.getElementById('btnCloseIoModal');

  const docSearchInput = document.getElementById('docSearchInput');
  const btnClearDocSearch = document.getElementById('btnClearDocSearch');
  const docCategoryChips = document.getElementById('docCategoryChips');

  const docsAdminToggle = document.getElementById('docsAdminToggle');
  const docsAdminPanel = document.getElementById('docsAdminPanel');
  const docsAdminChevron = document.getElementById('docsAdminChevron');
  const docsAdminPassword = document.getElementById('docsAdminPassword');
  const btnUnlockDocsAdmin = document.getElementById('btnUnlockDocsAdmin');
  const docsAdminGate = document.getElementById('docsAdminGate');
  const docsUploadSection = document.getElementById('docsUploadSection');

  const docsDropZone = document.getElementById('docsDropZone');
  const docsFileInput = document.getElementById('docsFileInput');
  const docsQueueContainer = document.getElementById('docsQueueContainer');
  const docsQueueCount = document.getElementById('docsQueueCount');
  const docsQueueTotalSize = document.getElementById('docsQueueTotalSize');
  const btnAddMoreDocs = document.getElementById('btnAddMoreDocs');
  const btnClearDocsQueue = document.getElementById('btnClearDocsQueue');
  const docsQueueList = document.getElementById('docsQueueList');

  const newDocCategory = document.getElementById('newDocCategory');
  const docsUploaderName = document.getElementById('docsUploaderName');
  const btnUploadDoc = document.getElementById('btnUploadDoc');
  const btnUploadDocText = document.getElementById('btnUploadDocText');
  let selectedDocFiles = [];

  // Load cached catalog metadata if available
  try {
    const cached = localStorage.getItem('ward_docs_catalog') || localStorage.getItem('ward_io_template');
    if (cached) {
      currentDocsCatalog = normalizeDocsCatalog(JSON.parse(cached));
    }
  } catch (e) {
    console.warn('Error reading cached documents catalog:', e);
  }

  // Preload saved uploader name
  if (docsUploaderName) {
    const savedName = localStorage.getItem('ward_author_name') || '';
    if (savedName) docsUploaderName.value = savedName;
  }

  // Open Modal
  if (openDocsModalBtn) {
    openDocsModalBtn.addEventListener('click', () => {
      if (!getCurrentUser()) {
        openLoginModal(true, 'กรุณาเข้าสู่ระบบก่อนเข้าใช้งานเอกสารและแบบฟอร์ม');
        return;
      }
      resetIdleTimer();
      openDocsModal();
      fetchDocsCatalogFromCloud();
    });
  }

  // Close Modal
  if (docsModalCloseBtn) docsModalCloseBtn.addEventListener('click', closeDocsModal);
  if (btnCloseDocsModal) btnCloseDocsModal.addEventListener('click', closeDocsModal);
  if (docsModal) {
    docsModal.addEventListener('click', (e) => {
      if (e.target === docsModal) closeDocsModal();
    });
  }

  // Edit Doc Modal controls
  const btnCancelEditDoc = document.getElementById('btnCancelEditDoc');
  const btnCloseEditDocModal = document.getElementById('btnCloseEditDocModal');
  const btnSaveEditDoc = document.getElementById('btnSaveEditDoc');
  const editDocModal = document.getElementById('editDocModal');

  if (btnCancelEditDoc) btnCancelEditDoc.addEventListener('click', closeEditDocModal);
  if (btnCloseEditDocModal) btnCloseEditDocModal.addEventListener('click', closeEditDocModal);
  if (btnSaveEditDoc) btnSaveEditDoc.addEventListener('click', saveEditedDocument);
  if (editDocModal) {
    editDocModal.addEventListener('click', (e) => {
      if (e.target === editDocModal) closeEditDocModal();
    });
  }

  // Search input events
  if (docSearchInput) {
    docSearchInput.addEventListener('input', (e) => {
      docSearchQuery = e.target.value;
      if (btnClearDocSearch) {
        btnClearDocSearch.style.display = docSearchQuery ? 'block' : 'none';
      }
      renderDocumentsList();
    });
  }

  if (btnClearDocSearch && docSearchInput) {
    btnClearDocSearch.addEventListener('click', () => {
      docSearchInput.value = '';
      docSearchQuery = '';
      btnClearDocSearch.style.display = 'none';
      renderDocumentsList();
      docSearchInput.focus();
    });
  }

  // Category filter chips
  if (docCategoryChips) {
    const chips = docCategoryChips.querySelectorAll('.doc-chip');
    chips.forEach(chip => {
      chip.addEventListener('click', () => {
        chips.forEach(c => c.classList.remove('active'));
        chip.classList.add('active');
        activeDocCategory = chip.getAttribute('data-category') || 'all';
        renderDocumentsList();
      });
    });
  }

  // Admin Accordion Toggle
  if (docsAdminToggle) {
    docsAdminToggle.addEventListener('click', () => {
      const isHidden = docsAdminPanel.style.display === 'none';
      docsAdminPanel.style.display = isHidden ? 'block' : 'none';
      if (docsAdminChevron) {
        docsAdminChevron.className = isHidden ? 'fa-solid fa-chevron-up' : 'fa-solid fa-chevron-down';
      }
      if (isHidden) {
        if (!isDocsAdminUnlocked && docsAdminPassword) {
          setTimeout(() => docsAdminPassword.focus(), 100);
        }
        setTimeout(() => {
          const body = document.querySelector('.docs-modal-body');
          const wrapper = document.querySelector('.docs-admin-wrapper');
          if (wrapper && body) {
            body.scrollTo({ top: wrapper.offsetTop - 10, behavior: 'smooth' });
          }
        }, 120);
      }
    });
  }

  // Unlock Admin (Secure SHA-256 hash comparison)
  async function tryUnlockAdmin() {
    const pass = (docsAdminPassword.value || '').trim();
    if (!pass) return;

    const hash = await sha256Hex(pass);
    const targetHash = '9416a40b88fff19d0365e4c29fb2cd67fcd5022216708f1fa258b1513c56a41d';

    if (hash === targetHash) {
      isDocsAdminUnlocked = true;
      docsAdminGate.style.display = 'none';
      docsUploadSection.style.display = 'block';
      showToast('🔓 ปลดล็อกสิทธิ์ Admin สำเร็จ สามารถจัดการและอัปโหลดเอกสารได้เลย', 'success');
      docsAdminPassword.value = '';
      renderDocumentsList();

      setTimeout(() => {
        const body = document.querySelector('.docs-modal-body');
        const uploadSec = document.getElementById('docsUploadSection');
        if (uploadSec && body) {
          body.scrollTo({ top: uploadSec.offsetTop - 15, behavior: 'smooth' });
        }
      }, 150);
    } else {
      showToast('❌ รหัสผ่านผู้ดูแลระบบไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง', 'error');
      docsAdminPassword.focus();
      docsAdminPassword.select();
    }
  }

  if (btnUnlockDocsAdmin) btnUnlockDocsAdmin.addEventListener('click', tryUnlockAdmin);
  if (docsAdminPassword) {
    docsAdminPassword.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        tryUnlockAdmin();
      }
    });
  }

  // Multi-File Helpers
  function formatDocSize(bytes) {
    if (!bytes || bytes <= 0) return '0 B';
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
  }

  function getFileIconMeta(filename) {
    const ext = (filename ? filename.split('.').pop() : '').toLowerCase();
    if (ext === 'xlsx' || ext === 'xls') return { cls: 'excel', icon: 'fa-file-excel' };
    if (ext === 'docx' || ext === 'doc') return { cls: 'word', icon: 'fa-file-word' };
    if (ext === 'pdf') return { cls: 'pdf', icon: 'fa-file-pdf' };
    return { cls: 'generic', icon: 'fa-file-lines' };
  }

  function renderDocUploadQueue() {
    if (!docsQueueContainer || !docsQueueList) return;

    if (selectedDocFiles.length === 0) {
      docsQueueContainer.style.display = 'none';
      if (btnUploadDoc) {
        btnUploadDoc.disabled = true;
        if (btnUploadDocText) btnUploadDocText.textContent = 'บันทึกและอัปโหลดเอกสารขึ้น Cloud';
      }
      return;
    }

    docsQueueContainer.style.display = 'flex';
    if (docsQueueCount) docsQueueCount.textContent = selectedDocFiles.length;

    const totalBytes = selectedDocFiles.reduce((acc, cur) => acc + (cur.file ? cur.file.size : 0), 0);
    if (docsQueueTotalSize) docsQueueTotalSize.textContent = `(${formatDocSize(totalBytes)})`;

    docsQueueList.innerHTML = '';
    selectedDocFiles.forEach((item, index) => {
      const row = document.createElement('div');
      row.className = 'doc-queue-item';

      const iconInfo = getFileIconMeta(item.file.name);

      row.innerHTML = `
        <div class="doc-queue-icon ${iconInfo.cls}">
          <i class="fa-solid ${iconInfo.icon}"></i>
        </div>
        <div class="doc-queue-file-meta">
          <span class="doc-queue-filename" title="${escapeHtml(item.file.name)}">${escapeHtml(item.file.name)}</span>
          <span class="doc-queue-filesize">${formatDocSize(item.file.size)}</span>
        </div>
        <div class="doc-queue-inputs">
          <span class="doc-queue-seq-tag" title="ลำดับที่อัตโนมัติ">#${item.order}</span>
          <input type="text" class="doc-queue-title-input" value="${escapeHtml(item.title)}" placeholder="ชื่อเอกสาร..." title="ชื่อที่จะแสดงในระบบ">
          <input type="text" class="doc-queue-version-input" value="${escapeHtml(item.version || 'v1.0')}" placeholder="v1.0" title="เลขเวอร์ชันอัตโนมัติ (แก้ไขได้)">
          <select class="doc-queue-cat-select" title="หมวดหมู่เอกสาร">
            <option value="แบบฟอร์มบันทึกทางการพยาบาล"${item.category === 'แบบฟอร์มบันทึกทางการพยาบาล' ? ' selected' : ''}>แบบฟอร์มบันทึก</option>
            <option value="แบบประเมินทางการพยาบาล"${item.category === 'แบบประเมินทางการพยาบาล' ? ' selected' : ''}>แบบประเมิน</option>
            <option value="แนวทาง CPG / หัตถการ"${item.category === 'แนวทาง CPG / หัตถการ' ? ' selected' : ''}>แนวทาง CPG</option>
            <option value="เอกสารและแบบฟอร์มทั่วไป"${item.category === 'เอกสารและแบบฟอร์มทั่วไป' ? ' selected' : ''}>เอกสารทั่วไป</option>
          </select>
        </div>
        <button type="button" class="btn-queue-remove" title="นำไฟล์นี้ออกจากรายการ"><i class="fa-solid fa-xmark"></i></button>
      `;

      // Input bindings
      const titleInput = row.querySelector('.doc-queue-title-input');
      if (titleInput) {
        titleInput.addEventListener('input', (e) => {
          item.title = e.target.value;
        });
      }

      const versionInput = row.querySelector('.doc-queue-version-input');
      if (versionInput) {
        versionInput.addEventListener('input', (e) => {
          item.version = e.target.value;
        });
      }

      const catSelect = row.querySelector('.doc-queue-cat-select');
      if (catSelect) {
        catSelect.addEventListener('change', (e) => {
          item.category = e.target.value;
        });
      }

      const removeBtn = row.querySelector('.btn-queue-remove');
      if (removeBtn) {
        removeBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          selectedDocFiles.splice(index, 1);
          renderDocUploadQueue();
        });
      }

      docsQueueList.appendChild(row);
    });

    if (btnUploadDoc) {
      btnUploadDoc.disabled = false;
      if (btnUploadDocText) {
        btnUploadDocText.textContent = selectedDocFiles.length === 1
          ? 'บันทึกและอัปโหลด 1 เอกสารขึ้น Cloud'
          : `บันทึกและอัปโหลดทั้ง ${selectedDocFiles.length} เอกสารขึ้น Cloud`;
      }
    }
  }

  function handleFilesAdded(fileList) {
    if (!fileList || fileList.length === 0) return;
    const defaultCat = (newDocCategory ? newDocCategory.value : '') || 'แบบฟอร์มบันทึกทางการพยาบาล';
    let oversizedCount = 0;
    let nextOrder = getNextDocOrder(currentDocsCatalog.documents);

    Array.from(fileList).forEach(file => {
      if (file.size > 15 * 1024 * 1024) {
        oversizedCount++;
        return;
      }

      const cleanTitle = file.name.replace(/\.[^/.]+$/, '');
      const existingDoc = currentDocsCatalog.documents.find(d => (d.filename || '').toLowerCase() === file.name.toLowerCase());
      const existingIdx = selectedDocFiles.findIndex(item => item.file.name.toLowerCase() === file.name.toLowerCase());

      const assignedOrder = existingDoc ? (existingDoc.order || nextOrder++) : nextOrder++;
      const assignedVersion = existingDoc
        ? bumpVersionString(existingDoc.doc_version || existingDoc.version)
        : extractOrGenerateVersion(file.name, null);

      const itemData = {
        id: 'queue_' + Date.now() + '_' + Math.random().toString(36).substr(2, 6),
        file: file,
        title: cleanTitle,
        category: defaultCat,
        order: assignedOrder,
        version: assignedVersion
      };

      if (existingIdx >= 0) {
        selectedDocFiles[existingIdx] = itemData;
      } else {
        selectedDocFiles.push(itemData);
      }
    });

    if (oversizedCount > 0) {
      showToast(`⚠️ ข้าม ${oversizedCount} ไฟล์ที่มีขนาดเกิน 15MB`, 'warning');
    }

    renderDocUploadQueue();
    setTimeout(() => {
      const body = document.querySelector('.docs-modal-body');
      const btn = document.getElementById('btnUploadDoc');
      if (btn && body) {
        btn.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    }, 120);
  }

  // File Selection & Drag-and-Drop
  if (docsDropZone && docsFileInput) {
    docsDropZone.addEventListener('click', () => {
      docsFileInput.value = '';
      docsFileInput.click();
    });

    docsDropZone.addEventListener('dragover', (e) => {
      e.preventDefault();
      docsDropZone.classList.add('drag-active');
    });

    docsDropZone.addEventListener('dragleave', (e) => {
      if (!docsDropZone.contains(e.relatedTarget)) {
        docsDropZone.classList.remove('drag-active');
      }
    });

    docsDropZone.addEventListener('drop', (e) => {
      e.preventDefault();
      docsDropZone.classList.remove('drag-active');
      if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        handleFilesAdded(e.dataTransfer.files);
      }
    });

    docsFileInput.addEventListener('change', () => {
      if (docsFileInput.files && docsFileInput.files.length > 0) {
        handleFilesAdded(docsFileInput.files);
      }
    });
  }

  // Queue toolbar buttons
  if (btnAddMoreDocs && docsFileInput) {
    btnAddMoreDocs.addEventListener('click', () => {
      docsFileInput.value = '';
      docsFileInput.click();
    });
  }

  if (btnClearDocsQueue) {
    btnClearDocsQueue.addEventListener('click', () => {
      selectedDocFiles = [];
      if (docsFileInput) docsFileInput.value = '';
      renderDocUploadQueue();
    });
  }

  // Upload Batch to Supabase Bed 100
  if (btnUploadDoc) {
    btnUploadDoc.addEventListener('click', async () => {
      if (selectedDocFiles.length === 0) return;

      const uploader = (docsUploaderName ? docsUploaderName.value.trim() : '') || 'Admin (เว็บ/มือถือ)';
      if (docsUploaderName && docsUploaderName.value.trim()) {
        try { localStorage.setItem('ward_author_name', docsUploaderName.value.trim()); } catch {}
      }

      btnUploadDoc.disabled = true;
      const totalCount = selectedDocFiles.length;

      try {
        const nowUtc = new Date().toISOString();
        let uploadedNames = [];

        for (let i = 0; i < totalCount; i++) {
          const item = selectedDocFiles[i];
          if (btnUploadDocText) {
            btnUploadDocText.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> กำลังอ่านไฟล์ (${i + 1}/${totalCount}) ${escapeHtml(item.file.name)}...`;
          }

          const base64Data = await readFileAsBase64(item.file);
          const ext = item.file.name.split('.').pop().toLowerCase();
          const docId = 'doc_' + Date.now() + '_' + i;
          const newDoc = {
            id: docId,
            order: parseInt(item.order, 10) || (currentDocsCatalog.documents.length + 1),
            seq: parseInt(item.order, 10) || (currentDocsCatalog.documents.length + 1),
            doc_version: (item.version || '').trim() || 'v1.0',
            version: (item.version || '').trim() || 'v1.0',
            title: (item.title || '').trim() || item.file.name.replace(/\.[^/.]+$/, ''),
            category: item.category || 'แบบฟอร์มบันทึกทางการพยาบาล',
            filename: item.file.name,
            download_name: item.file.name,
            file_type: ext,
            size: item.file.size,
            updated_at: nowUtc,
            updated_by: uploader,
            base64: base64Data
          };

          const existingIdx = currentDocsCatalog.documents.findIndex(d => d.filename.toLowerCase() === item.file.name.toLowerCase());
          if (existingIdx >= 0) {
            currentDocsCatalog.documents[existingIdx] = newDoc;
          } else {
            currentDocsCatalog.documents.push(newDoc);
          }
          uploadedNames.push(`#${newDoc.order} ${newDoc.title} (${newDoc.doc_version})`);
        }

        // Sort by order ascending
        currentDocsCatalog.documents.sort((a, b) => {
          const diff = (a.order || 0) - (b.order || 0);
          return diff !== 0 ? diff : (a.title || '').localeCompare(b.title || '');
        });

        if (btnUploadDocText) {
          btnUploadDocText.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> กำลังบันทึก ${totalCount} เอกสารขึ้น Cloud...`;
        }

        const jsonString = JSON.stringify(currentDocsCatalog);

        // 1. Single atomic Supabase PATCH to bed_notes row 100
        const patchRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.100`, {
          method: 'PATCH',
          headers: {
            'apikey': SUPABASE_KEY,
            'Authorization': `Bearer ${SUPABASE_KEY}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            content: jsonString,
            updated_at: nowUtc,
            updated_by: uploader
          })
        });

        if (!patchRes.ok) throw new Error(`Supabase PATCH failed: ${patchRes.status}`);

        // 2. Audit log
        fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
          method: 'POST',
          headers: {
            'apikey': SUPABASE_KEY,
            'Authorization': `Bearer ${SUPABASE_KEY}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            bed_number: 100,
            reason: `อัปโหลดเอกสารวอร์ด (${totalCount} ไฟล์) โดย ${uploader}`,
            content: `รายการ: ${uploadedNames.join(', ')}`,
            char_count: totalCount,
            created_at: nowUtc
          })
        }).catch(err => console.warn('History snapshot error:', err));

        // 3. Cache locally & refresh UI
        try { localStorage.setItem('ward_docs_catalog', jsonString); } catch {}
        renderDocumentsList();

        selectedDocFiles = [];
        if (docsFileInput) docsFileInput.value = '';
        renderDocUploadQueue();

        showToast(`✅ อัปโหลดเอกสาร ${totalCount} รายการขึ้น Cloud สำเร็จเรียบร้อย! ทุกเครื่องในวอร์ดจะเห็นเอกสารใหม่ทันที`, 'success');

      } catch (err) {
        console.error('Batch upload documents error:', err);
        showToast('❌ อัปโหลดล้มเหลว กรุณาตรวจสอบการเชื่อมต่ออินเทอร์เน็ต', 'error');
      } finally {
        if (btnUploadDoc) {
          btnUploadDoc.disabled = selectedDocFiles.length === 0;
          if (btnUploadDocText) {
            btnUploadDocText.innerHTML = '<i class="fa-solid fa-cloud-arrow-up"></i> บันทึกและอัปโหลดเอกสารขึ้น Cloud';
          }
        }
      }
    });
  }

  // Initial render from cache and silent cloud fetch
  renderDocumentsList();
  // Fetch the Base64 document catalog only when its menu is opened.
}

function openDocsModal() {
  const modal = document.getElementById('docsModal') || document.getElementById('ioModal');
  if (modal) {
    document.body.classList.add('modal-open');
    modal.classList.add('open');
    modal.setAttribute('aria-hidden', 'false');
    renderDocumentsList();

    // Universal Wheel Event Forwarding: Allows scrolling even when cursor is on backdrop, header, or footer
    if (!modal._hasWheelForwarder) {
      modal._hasWheelForwarder = true;
      modal.addEventListener('wheel', (e) => {
        const body = modal.querySelector('.docs-modal-body');
        if (!body) return;
        const target = e.target;
        if (target && target.closest('.docs-queue-list')) {
          const ql = target.closest('.docs-queue-list');
          if (ql && ql.scrollHeight > ql.clientHeight) return;
        }
        if (!target.closest('.docs-modal-body')) {
          if (e.deltaY !== 0) {
            body.scrollTop += e.deltaY;
            e.preventDefault();
          }
        }
      }, { passive: false });
    }

    // Keyboard Arrow / Page Navigation for instant accessibility
    if (!modal._hasKeyNav) {
      modal._hasKeyNav = true;
      window.addEventListener('keydown', (e) => {
        if (!modal.classList.contains('open')) return;
        const tag = (document.activeElement ? document.activeElement.tagName : '').toLowerCase();
        if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
        const body = modal.querySelector('.docs-modal-body');
        if (!body) return;

        if (e.key === 'ArrowDown' || e.key === 'PageDown') {
          e.preventDefault();
          body.scrollBy({ top: e.key === 'PageDown' ? 250 : 80, behavior: 'smooth' });
        } else if (e.key === 'ArrowUp' || e.key === 'PageUp') {
          e.preventDefault();
          body.scrollBy({ top: e.key === 'PageUp' ? -250 : -80, behavior: 'smooth' });
        }
      });
    }
  }
}

function closeDocsModal() {
  const modal = document.getElementById('docsModal') || document.getElementById('ioModal');
  if (modal) {
    document.body.classList.remove('modal-open');
    modal.classList.remove('open');
    modal.setAttribute('aria-hidden', 'true');
  }
}

// Backward compatibility aliases
function initIoTemplateSystem() { initDocumentsSystem(); }
function openIoModal() { openDocsModal(); }
function closeIoModal() { closeDocsModal(); }
function fetchIoTemplateFromCloud(silent) { fetchDocsCatalogFromCloud(silent); }
function downloadIoTemplateFile() {
  const ioDoc = currentDocsCatalog.documents.find(d => d.id === 'doc_io_template') || currentDocsCatalog.documents[0] || DEFAULT_IO_DOC;
  downloadDocument(ioDoc);
}

function renderDocumentsList() {
  const container = document.getElementById('docsListGrid');
  if (!container) return;

  const q = (docSearchQuery || '').trim().toLowerCase();
  const cat = activeDocCategory;

  const filtered = currentDocsCatalog.documents.filter(doc => {
    // Category check
    if (cat !== 'all') {
      const docCat = (doc.category || '').toLowerCase();
      if (!docCat.includes(cat.toLowerCase())) return false;
    }
    // Search query check
    if (q) {
      const matchTitle = (doc.title || '').toLowerCase().includes(q);
      const matchFile = (doc.filename || '').toLowerCase().includes(q);
      const matchCat = (doc.category || '').toLowerCase().includes(q);
      if (!matchTitle && !matchFile && !matchCat) return false;
    }
    return true;
  });

  if (filtered.length === 0) {
    container.innerHTML = `
      <div class="docs-empty-state">
        <i class="fa-solid fa-folder-open"></i>
        <p style="margin: 8px 0 4px; font-weight: 600; color: var(--text-main);">ไม่พบเอกสารที่ค้นหา</p>
        <span style="font-size: 0.8rem; color: var(--text-dim);">ลองเปลี่ยนคำค้นหา หรือเลือกหมวดหมู่อื่นดูครับ</span>
      </div>
    `;
    return;
  }

  container.innerHTML = '';
  filtered.forEach(doc => {
    const card = document.createElement('div');
    card.className = 'doc-item-card';

    // File type detection
    const ext = (doc.file_type || (doc.filename ? doc.filename.split('.').pop() : '')).toLowerCase();
    let iconClass = 'generic';
    let iconFa = 'fa-file-lines';
    if (ext === 'xlsx' || ext === 'xls') {
      iconClass = 'excel';
      iconFa = 'fa-file-excel';
    } else if (ext === 'docx' || ext === 'doc') {
      iconClass = 'word';
      iconFa = 'fa-file-word';
    } else if (ext === 'pdf') {
      iconClass = 'pdf';
      iconFa = 'fa-file-pdf';
    }

    const sizeStr = doc.size ? `${(doc.size / 1024).toFixed(1)} KB` : '-- KB';
    const timeStr = doc.updated_at ? formatDateTimeThai(doc.updated_at) : 'ล่าสุด';
    const uploaderStr = escapeHtml(doc.updated_by || 'Admin');
    const titleStr = escapeHtml(doc.title || doc.filename || 'เอกสารประจำวอร์ด');
    const filenameStr = escapeHtml(doc.filename || '');
    const categoryStr = escapeHtml(doc.category || 'เอกสารทั่วไป');

    card.innerHTML = `
      <div class="doc-card-icon ${iconClass}">
        <i class="fa-solid ${iconFa}"></i>
      </div>
      <div class="doc-card-details">
        <div class="doc-card-title-row">
          <span class="doc-card-seq-badge">#${doc.order || 1}</span>
          <span class="doc-card-title">${titleStr}</span>
          <span class="doc-card-version-badge">${escapeHtml(doc.doc_version || doc.version || 'v1.0')}</span>
          <span class="doc-card-badge">${categoryStr}</span>
        </div>
        <div class="doc-card-meta">
          <span class="io-meta-tag"><i class="fa-solid fa-paperclip"></i> ${filenameStr}</span>
          <span class="io-meta-tag"><i class="fa-solid fa-hard-drive"></i> ${sizeStr}</span>
          <span class="io-meta-tag"><i class="fa-regular fa-clock"></i> ${timeStr}</span>
          <span class="io-meta-tag"><i class="fa-solid fa-user-check"></i> ${uploaderStr}</span>
        </div>
      </div>
      <div class="doc-card-actions">
        <button type="button" class="btn-doc-download" data-doc-id="${doc.id}">
          <i class="fa-solid fa-cloud-arrow-down"></i>
          <span>ดาวน์โหลด</span>
        </button>
        ${isDocsAdminUnlocked ? `
          <button type="button" class="btn-doc-edit" data-doc-id="${doc.id}" title="แก้ไขชื่อและหมวดหมู่">
            <i class="fa-solid fa-pen-to-square"></i>
            <span>แก้ไข</span>
          </button>
          <button type="button" class="btn-doc-delete" data-doc-id="${doc.id}" title="ลบเอกสารนี้ออกจากระบบ">
            <i class="fa-solid fa-trash-can"></i>
          </button>
        ` : ''}
      </div>
    `;

    // Download Handler
    const dlBtn = card.querySelector('.btn-doc-download');
    if (dlBtn) {
      dlBtn.addEventListener('click', () => downloadDocument(doc));
    }

    // Edit Handler (Admin Mode)
    const editBtn = card.querySelector('.btn-doc-edit');
    if (editBtn) {
      editBtn.addEventListener('click', () => openEditDocModal(doc));
    }

    // Delete Handler (Admin Mode)
    const delBtn = card.querySelector('.btn-doc-delete');
    if (delBtn) {
      delBtn.addEventListener('click', () => confirmDeleteDocument(doc));
    }

    container.appendChild(card);
  });
}

let docsFetchBusy = false;
let docsVersion = null;
let docsFullFetchAt = 0;
async function fetchDocsCatalogFromCloud(silent = false) {
  if (!getCurrentUser() || docsFetchBusy) return;
  docsFetchBusy = true;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15000);
  try {
    const meta = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.100&select=updated_at`, {
      signal: controller.signal,
      headers: { apikey: SUPABASE_KEY, Authorization: `Bearer ${SUPABASE_KEY}` }
    });
    if (!meta.ok) throw new Error(`HTTP ${meta.status}`);
    const version = JSON.stringify(await meta.json());
    if (version === docsVersion && Date.now() - docsFullFetchAt < 300000) return;
    const res = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.100&select=content,updated_at,updated_by`, {
      method: 'GET',
      signal: controller.signal,
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });

    if (!res.ok) return;

    const rows = await res.json();
    if (rows && rows.length > 0 && rows[0].content) {
      const parsed = JSON.parse(rows[0].content);
      currentDocsCatalog = normalizeDocsCatalog(parsed);
      try {
        localStorage.setItem('ward_docs_catalog', JSON.stringify(currentDocsCatalog));
      } catch {}
      docsVersion = version;
      docsFullFetchAt = Date.now();
      renderDocumentsList();
    }
  } catch (err) {
    if (!silent) console.warn('Could not fetch latest documents catalog:', err);
  } finally {
    clearTimeout(timeout);
    docsFetchBusy = false;
  }
}

function downloadDocument(doc) {
  if (!doc) return;
  const downloadName = doc.download_name || doc.filename || 'ward_document';

  if (doc.base64) {
    try {
      const byteChars = atob(doc.base64);
      const byteNumbers = new Array(byteChars.length);
      for (let i = 0; i < byteChars.length; i++) {
        byteNumbers[i] = byteChars.charCodeAt(i);
      }
      const byteArray = new Uint8Array(byteNumbers);

      const ext = (doc.file_type || (doc.filename ? doc.filename.split('.').pop() : '')).toLowerCase();
      let mime = 'application/octet-stream';
      if (ext === 'xlsx') mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
      else if (ext === 'xls') mime = 'application/vnd.ms-excel';
      else if (ext === 'docx') mime = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
      else if (ext === 'doc') mime = 'application/msword';
      else if (ext === 'pdf') mime = 'application/pdf';

      const blob = new Blob([byteArray], { type: mime });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = downloadName;
      document.body.appendChild(a);
      a.click();
      setTimeout(() => {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      }, 200);

      showToast(`📥 ดาวน์โหลดเอกสาร [${downloadName}] เรียบร้อยแล้ว`, 'success');
      return;
    } catch (e) {
      console.warn('Base64 decode failed, fallback to static URL:', e);
    }
  }

  // Fallback static URL
  const staticUrl = doc.static_url || (doc.filename === 'แบบฟอร์ม IO.xlsx' ? 'templates/IO_Template.xlsx' : null);
  if (staticUrl) {
    const a = document.createElement('a');
    a.href = staticUrl;
    a.download = downloadName;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => document.body.removeChild(a), 200);
    showToast(`📥 ดาวน์โหลดเอกสาร [${downloadName}] เรียบร้อยแล้ว`, 'success');
  } else {
    showToast('⚠️ ไม่พบข้อมูลไฟล์สำหรับดาวน์โหลด', 'warning');
  }
}

async function uploadNewDocument(file, title, category, uploader) {
  const base64Data = await readFileAsBase64(file);
  const nowUtc = new Date().toISOString();
  const ext = file.name.split('.').pop().toLowerCase();
  const docId = 'doc_' + Date.now();

  const newDoc = {
    id: docId,
    title: title || file.name.replace(/\.[^/.]+$/, ''),
    category: category || 'แบบฟอร์มบันทึกทางการพยาบาล',
    filename: file.name,
    download_name: file.name,
    file_type: ext,
    size: file.size,
    updated_at: nowUtc,
    updated_by: uploader || 'Admin',
    base64: base64Data
  };

  // If a doc with same filename exists, replace it; otherwise add to beginning
  const existingIdx = currentDocsCatalog.documents.findIndex(d => d.filename.toLowerCase() === file.name.toLowerCase());
  if (existingIdx >= 0) {
    currentDocsCatalog.documents[existingIdx] = newDoc;
  } else {
    currentDocsCatalog.documents.unshift(newDoc);
  }

  const jsonString = JSON.stringify(currentDocsCatalog);

  // 1. Save to Supabase row 100
  const patchRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.100`, {
    method: 'PATCH',
    headers: {
      'apikey': SUPABASE_KEY,
      'Authorization': `Bearer ${SUPABASE_KEY}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      content: jsonString,
      updated_at: nowUtc,
      updated_by: uploader
    })
  });

  if (!patchRes.ok) throw new Error(`Supabase PATCH failed: ${patchRes.status}`);

  // 2. Audit trail in bed_history
  fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
    method: 'POST',
    headers: {
      'apikey': SUPABASE_KEY,
      'Authorization': `Bearer ${SUPABASE_KEY}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      bed_number: 100,
      reason: `อัปโหลดเอกสารวอร์ด [${newDoc.title}] โดย ${uploader}`,
      content: `ไฟล์: ${file.name} (${(file.size / 1024).toFixed(1)} KB) หมวด: ${newDoc.category}`,
      char_count: file.size,
      created_at: nowUtc
    })
  }).catch(err => console.warn('History snapshot error:', err));

  // 3. Cache locally
  try {
    localStorage.setItem('ward_docs_catalog', jsonString);
  } catch {}

  renderDocumentsList();
}

async function confirmDeleteDocument(doc) {
  if (!confirm(`คุณต้องการลบเอกสาร "${doc.title || doc.filename}" ออกจากคลังใช่หรือไม่?`)) return;

  const idx = currentDocsCatalog.documents.findIndex(d => d.id === doc.id);
  if (idx < 0) return;

  currentDocsCatalog.documents.splice(idx, 1);
  if (currentDocsCatalog.documents.length === 0) {
    currentDocsCatalog.documents.push(DEFAULT_IO_DOC);
  }

  const nowUtc = new Date().toISOString();
  const jsonString = JSON.stringify(currentDocsCatalog);

  try {
    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.100`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: jsonString,
        updated_at: nowUtc,
        updated_by: 'Admin'
      })
    });
    try { localStorage.setItem('ward_docs_catalog', jsonString); } catch {}
    renderDocumentsList();
    showToast(`🗑️ ลบเอกสาร [${doc.title || doc.filename}] เรียบร้อยแล้ว`, 'info');
  } catch (err) {
    showToast('❌ ไม่สามารถลบเอกสารได้ กรุณาลองใหม่อีกครั้ง', 'error');
  }
}

let currentlyEditingDoc = null;

function openEditDocModal(doc) {
  currentlyEditingDoc = doc;
  const modal = document.getElementById('editDocModal');
  const txtFilename = document.getElementById('editDocFilename');
  const numOrder = document.getElementById('editDocOrder');
  const txtVersion = document.getElementById('editDocVersion');
  const txtTitle = document.getElementById('editDocTitle');
  const selCat = document.getElementById('editDocCategory');
  if (!modal || !doc) return;

  if (txtFilename) txtFilename.value = doc.filename || '';
  if (numOrder) numOrder.value = doc.order || 1;
  if (txtVersion) txtVersion.value = doc.doc_version || doc.version || 'v1.0';
  if (txtTitle) txtTitle.value = doc.title || doc.filename || '';
  if (selCat) {
    const opts = Array.from(selCat.options).map(o => o.value);
    if (opts.includes(doc.category)) {
      selCat.value = doc.category;
    } else {
      selCat.selectedIndex = 0;
    }
  }

  modal.classList.add('open');
  modal.setAttribute('aria-hidden', 'false');
  if (txtTitle) {
    setTimeout(() => {
      txtTitle.focus();
      txtTitle.select();
    }, 150);
  }
}

function closeEditDocModal() {
  const modal = document.getElementById('editDocModal');
  if (modal) {
    modal.classList.remove('open');
    modal.setAttribute('aria-hidden', 'true');
  }
  currentlyEditingDoc = null;
}

async function saveEditedDocument() {
  if (!currentlyEditingDoc) return;
  const numOrder = document.getElementById('editDocOrder');
  const txtVersion = document.getElementById('editDocVersion');
  const txtTitle = document.getElementById('editDocTitle');
  const selCat = document.getElementById('editDocCategory');

  const newOrder = numOrder ? parseInt(numOrder.value, 10) : currentlyEditingDoc.order;
  const newVersion = (txtVersion ? txtVersion.value : '').trim() || 'v1.0';
  const newTitle = (txtTitle ? txtTitle.value : '').trim();
  const newCat = selCat ? selCat.value : currentlyEditingDoc.category;

  if (!newTitle) {
    showToast('กรุณาระบุชื่อเอกสาร', 'warning');
    if (txtTitle) txtTitle.focus();
    return;
  }

  const btnSave = document.getElementById('btnSaveEditDoc');
  if (btnSave) {
    btnSave.disabled = true;
    btnSave.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังบันทึก...';
  }

  const oldTitle = currentlyEditingDoc.title;
  currentlyEditingDoc.order = !isNaN(newOrder) && newOrder > 0 ? newOrder : (currentlyEditingDoc.order || 1);
  currentlyEditingDoc.seq = currentlyEditingDoc.order;
  currentlyEditingDoc.doc_version = newVersion;
  currentlyEditingDoc.version = newVersion;
  currentlyEditingDoc.title = newTitle;
  currentlyEditingDoc.category = newCat;
  currentlyEditingDoc.updated_at = new Date().toISOString();

  currentDocsCatalog.documents.sort((a, b) => {
    const diff = (a.order || 0) - (b.order || 0);
    return diff !== 0 ? diff : (a.title || '').localeCompare(b.title || '');
  });

  const jsonString = JSON.stringify(currentDocsCatalog);

  try {
    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.100`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: jsonString,
        updated_at: currentlyEditingDoc.updated_at,
        updated_by: 'Admin'
      })
    });
    try { localStorage.setItem('ward_docs_catalog', jsonString); } catch {}
    closeEditDocModal();
    renderDocumentsList();
    showToast(`✏️ บันทึกการแก้ไข [${newTitle}] เรียบร้อยแล้ว`, 'success');
  } catch (err) {
    showToast('❌ บันทึกการแก้ไขล้มเหลว กรุณาลองใหม่อีกครั้ง', 'error');
  } finally {
    if (btnSave) {
      btnSave.disabled = false;
      btnSave.innerHTML = '<i class="fa-solid fa-floppy-disk"></i> บันทึกการแก้ไข';
    }
  }
}

function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result;
      const base64 = result.split(',')[1];
      resolve(base64);
    };
    reader.onerror = (error) => reject(error);
    reader.readAsDataURL(file);
  });
}

async function sha256Hex(str) {
  try {
    const buf = new TextEncoder().encode(str);
    const hash = await crypto.subtle.digest('SHA-256', buf);
    return Array.from(new Uint8Array(hash)).map(b => b.toString(16).padStart(2, '0')).join('');
  } catch (e) {
    return '';
  }
}
