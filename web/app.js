
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

const SUPABASE_URL = "https://mhzpurmhrqutxdhmsday.supabase.co";
const SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1oenB1cm1ocnF1dHhkaG1zZGF5Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTA2OTM1NjAsImV4cCI6MjEwNjI2OTU2MH0.A9a4sox0YUBKlWkEcaInqQOb8EA0yzl99uwY_cg-kyo";

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
let isSaving = false;

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
  initAuthorName();
  initEmptyBeds();
  loadCachedBeds();
  setupEventListeners();
  
  // Initial fetch and start polling every 3 seconds
  fetchAllBeds();
  initClinicalTemplates();
  pollTimer = setInterval(fetchAllBeds, 3000);
});

// Initialize 30 empty beds
function initEmptyBeds() {
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
    const cached = localStorage.getItem('ward_bed_notes_cache');
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
  const savedAuthor = localStorage.getItem('ward_author_name') || '';
  if (savedAuthor) authorInput.value = savedAuthor;
}

function saveAuthorName(name) {
  if (name) localStorage.setItem('ward_author_name', name.trim());
}

// ==========================================
// Event Listeners
// ==========================================
function setupEventListeners() {
  themeToggleBtn.addEventListener('click', toggleTheme);
  
  refreshBtn.addEventListener('click', () => {
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

  // View Mode Toggle (Compact vs Expanded)
  initViewMode();

  // Keyboard Shortcuts
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      if (templateModal && templateModal.classList.contains('open')) {
        closeTemplateModal();
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
  try {
    const res = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?select=bed_number,content,updated_at,updated_by&order=bed_number.asc`, {
      method: 'GET',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`
      }
    });

    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();
    setCloudStatus(true);

    // Track changed beds for pulse animation
    const changedBeds = new Set();
    data.forEach(item => {
      const prev = prevContentMap.get(item.bed_number);
      if (prev !== undefined && prev !== (item.content || '')) {
        changedBeds.add(item.bed_number);
      }
      prevContentMap.set(item.bed_number, item.content || '');

      const idx = bedsData.findIndex(b => b.bed_number === item.bed_number);
      if (idx !== -1) {
        bedsData[idx] = item;
      }
    });

    // Save to localStorage cache
    try {
      localStorage.setItem('ward_bed_notes_cache', JSON.stringify(bedsData));
    } catch {}

    renderBeds(changedBeds);
    updateStats();

  } catch (err) {
    console.warn('Supabase fetch error:', err);
    setCloudStatus(false);
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

function saveEditDraft(bedNum) {
  const b = bedNum || activeBedNumber;
  if (!b) return;
  try {
    const val = noteTextarea.value;
    if (val !== originalEditContent && val.trim().length > 0) {
      localStorage.setItem(`ward_bed_draft_${b}`, val);
    } else {
      localStorage.removeItem(`ward_bed_draft_${b}`);
    }
  } catch {}
}

function clearEditDraft(bedNum) {
  const b = bedNum || activeBedNumber;
  if (!b) return;
  try {
    localStorage.removeItem(`ward_bed_draft_${b}`);
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
    const draft = localStorage.getItem(`ward_bed_draft_${bedNum}`);
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
  const author = authorInput.value.trim() || 'มือถือ/เว็บ';
  saveAuthorName(author);

  btnSaveBedNote.disabled = true;
  btnSaveBedNote.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> กำลังบันทึก...';

  try {
    const nowUtc = new Date().toISOString();
    
    // 1. Update bed_notes table
    const patchRes = await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${activeBedNumber}`, {
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
          bed_number: activeBedNumber,
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
          bed_number: activeBedNumber,
          reason: 'ก่อนล้างเตียง (Clear จากมือถือ/เว็บ)',
          content: oldContent,
          char_count: oldContent.length,
          created_at: nowUtc
        })
      });
    }

    // Clear bed_notes
    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${activeBedNumber}`, {
      method: 'PATCH',
      headers: {
        'apikey': SUPABASE_KEY,
        'Authorization': `Bearer ${SUPABASE_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        content: '',
        updated_at: nowUtc,
        updated_by: authorInput.value.trim() || 'มือถือ/เว็บ'
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
    const res = await fetch(`${SUPABASE_URL}/rest/v1/bed_history?bed_number=eq.${bedNum}&order=created_at.desc&limit=50`, {
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

    await fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${activeBedNumber}`, {
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
    const author = (authorInput ? authorInput.value.trim() : '') || 'มือถือ/เว็บ';
    const nowUtc = new Date().toISOString();

    // 1. History Snapshots
    const histPromises = [];
    if (fromContent.trim()) {
      histPromises.push(fetch(`${SUPABASE_URL}/rest/v1/bed_history`, {
        method: 'POST',
        headers: { 'apikey': SUPABASE_KEY, 'Authorization': `Bearer ${SUPABASE_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bed_number: fromBedNum,
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
          bed_number: targetBedNum,
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
      fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${fromBedNum}`, {
        method: 'PATCH',
        headers: { 'apikey': SUPABASE_KEY, 'Authorization': `Bearer ${SUPABASE_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: newFromContent, updated_at: nowUtc, updated_by: author })
      }),
      fetch(`${SUPABASE_URL}/rest/v1/bed_notes?bed_number=eq.${targetBedNum}`, {
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

// Fetch and initialize templates
async function initClinicalTemplates() {
  try {
    const res = await fetch('templates.json');
    if (!res.ok) throw new Error('Cannot load templates.json');
    templateCategories = await res.json();
    
    // Flatten templates into searchable array
    allTemplates = [];
    templateCategories.forEach((cat, catIdx) => {
      cat.items.forEach(item => {
        allTemplates.push({
          shortcut: item.shortcut,
          title: item.title,
          content: item.content,
          category: cat.category,
          catIndex: catIdx
        });
      });
    });

    renderTemplateCategoryPills();
    renderTemplatesList();
    if (allTemplates.length > 0) {
      selectTemplate(allTemplates[0], false);
    }
  } catch (err) {
    console.warn('Failed to fetch templates.json', err);
  }

  setupTemplateEventListeners();
}

// Setup Event Listeners for Templates
function setupTemplateEventListeners() {
  if (openTemplateLibraryBtn) {
    openTemplateLibraryBtn.addEventListener('click', () => openTemplateModal('navbar'));
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
  { icon: "fa-kit-medical", label: "วิกฤต & ช่วยชีวิต (Critical Care)" }
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

  if (templateRawText) templateRawText.value = item.content;

  // Format DAR
  if (previewFormattedView) {
    previewFormattedView.innerHTML = formatDARHtml(item.content);
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
    textToCopy = selectedTemplate.content;
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

  let contentToApply = selectedTemplate.content;
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
