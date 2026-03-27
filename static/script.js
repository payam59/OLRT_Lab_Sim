// 1. Fetch and Display Grid
async function fetchAssets() {
    try {
        const response = await fetch('/api/assets');
        const assets = await response.json();
        const grid = document.getElementById('assetGrid');

        grid.innerHTML = assets.map(a => {
            const isDigital = a.sub_type === "Digital";
            const status = isDigital ? (a.current_value >= 0.5 ? 'ON' : 'OFF') : a.current_value.toFixed(2);

            return `
            <div class="col-md-4 col-lg-3 mb-4">
                <div class="card asset-card p-3 shadow-sm ${a.manual_override ? 'overridden' : ''}">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <span class="badge bg-dark">${a.protocol.toUpperCase()}</span>
                        <div class="d-flex gap-2">
                            <button class="btn btn-link text-primary p-0" onclick="openEditModal('${a.name}')"><i class="fas fa-edit"></i></button>
                            <button class="btn btn-link text-danger p-0" onclick="deleteAsset('${a.name}')"><i class="fas fa-times"></i></button>
                        </div>
                    </div>
                    <div class="icon-container text-center my-2">
                        <i class="fas ${a.icon} fa-2x"></i>
                    </div>
                    <div class="text-center">
                        <h6 class="mb-0 fw-bold">${a.name}</h6>
                        <small class="text-muted d-block mb-1">${isDigital ? (a.is_normally_open ? 'N.O.' : 'N.C.') : 'Analog'}</small>
                        <h2 class="value-display my-2">${status}</h2>
                    </div>
                    <div class="d-flex gap-2 mt-2">
                        <button class="btn btn-sm btn-outline-warning w-100" onclick="sendOverride('${a.name}')">Inject</button>
                        <button class="btn btn-sm btn-outline-success w-100" onclick="sendRelease('${a.name}')">Auto</button>
                    </div>
                </div>
            </div>`;
        }).join('');
    } catch (err) { console.error("Sync error:", err); }
}

// 2. Toggle Visibility
function toggleFields(prefix = '') {
    const subTypeEl = document.getElementById(prefix + 'sub_type');
    if (!subTypeEl) return;

    const subType = subTypeEl.value;
    const isDigital = subType === 'Digital';

    const analogFields = document.getElementById(prefix + 'analog_fields');
    const digitalFields = document.getElementById(prefix + 'digital_fields');

    if (analogFields) analogFields.style.display = isDigital ? 'none' : 'block';
    if (digitalFields) digitalFields.style.display = isDigital ? 'block' : 'none';
}

// 3. CRUD Operations
async function saveNewAsset() {
    const data = {
        name: document.getElementById('name').value,
        type: document.getElementById('type').value,
        sub_type: document.getElementById('sub_type').value,
        protocol: document.getElementById('protocol').value,
        address: parseInt(document.getElementById('addr').value),
        min_range: parseFloat(document.getElementById('min').value) || 0,
        max_range: parseFloat(document.getElementById('max').value) || 100,
        drift_rate: parseFloat(document.getElementById('drift').value) || 0,
        icon: document.getElementById('icon').value,
        bacnet_port: parseInt(document.getElementById('bac_port').value) || 47808,
        bacnet_device_id: parseInt(document.getElementById('bac_id').value) || 1234,
        is_normally_open: parseInt(document.getElementById('logic_state').value)
    };

    if (!data.name) { alert("Name required"); return; }

    const res = await fetch('/api/assets', {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify(data)
    });

    if (res.ok) {
        bootstrap.Modal.getInstance(document.getElementById('addModal')).hide();
        fetchAssets();
    }
}

async function openEditModal(name) {
    try {
        const res = await fetch(`/api/assets/${name}`);
        if (!res.ok) throw new Error("Asset not found");
        const a = await res.json();

        document.getElementById('edit_name').value = a.name;
        document.getElementById('edit_type').value = a.type;
        document.getElementById('edit_sub_type').value = a.sub_type;
        document.getElementById('edit_protocol').value = a.protocol;
        document.getElementById('edit_addr').value = a.address;
        document.getElementById('edit_icon').value = a.icon;
        document.getElementById('edit_min').value = a.min_range;
        document.getElementById('edit_max').value = a.max_range;
        document.getElementById('edit_drift').value = a.drift_rate;
        document.getElementById('edit_logic_state').value = a.is_normally_open;
        document.getElementById('edit_bac_port').value = a.bacnet_port;
        document.getElementById('edit_bac_id').value = a.bacnet_device_id;

        toggleFields('edit_');
        new bootstrap.Modal(document.getElementById('editModal')).show();
    } catch (e) {
        console.error("Error populating modal:", e);
    }
}

async function saveAssetEdit() {
    const name = document.getElementById('edit_name').value;
    const data = {
        name: name,
        type: document.getElementById('edit_type').value,
        sub_type: document.getElementById('edit_sub_type').value,
        protocol: document.getElementById('edit_protocol').value,
        address: parseInt(document.getElementById('edit_addr').value),
        min_range: parseFloat(document.getElementById('edit_min').value) || 0,
        max_range: parseFloat(document.getElementById('edit_max').value) || 100,
        drift_rate: parseFloat(document.getElementById('edit_drift').value) || 0,
        icon: document.getElementById('edit_icon').value,
        bacnet_port: parseInt(document.getElementById('edit_bac_port').value) || 47808,
        bacnet_device_id: parseInt(document.getElementById('edit_bac_id').value) || 1234,
        is_normally_open: parseInt(document.getElementById('edit_logic_state').value)
    };

    const res = await fetch(`/api/assets/${name}`, {
        method: 'PUT',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify(data)
    });

    if (res.ok) {
        bootstrap.Modal.getInstance(document.getElementById('editModal')).hide();
        fetchAssets();
    }
}

async function deleteAsset(name) {
    if (confirm(`Remove ${name}?`)) {
        await fetch(`/api/assets/${name}`, { method: 'DELETE' });
        fetchAssets();
    }
}

async function sendOverride(name) {
    const val = prompt("Manual Value (0/1 for Digital):");
    if (val !== null) {
        await fetch(`/api/override/${name}?value=${val}`, { method: 'PUT' });
        fetchAssets();
    }
}

async function sendRelease(name) {
    await fetch(`/api/release/${name}`, { method: 'PUT' });
    fetchAssets();
}

// Global scope binding
window.saveNewAsset = saveNewAsset;
window.saveAssetEdit = saveAssetEdit;
window.deleteAsset = deleteAsset;
window.sendOverride = sendOverride;
window.sendRelease = sendRelease;
window.openEditModal = openEditModal;
window.toggleFields = toggleFields;

document.addEventListener('DOMContentLoaded', () => {
    fetchAssets();
    setInterval(fetchAssets, 2000); // Increased interval slightly for stability
});