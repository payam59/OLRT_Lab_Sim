// 1. Fetch and Display Assets in the Grid
async function fetchAssets() {
    try {
        const response = await fetch('/api/assets');
        const assets = await response.json();
        const grid = document.getElementById('assetGrid');

        grid.innerHTML = assets.map(a => `
            <div class="col-md-4 col-lg-3 mb-4">
                <div class="card asset-card p-3 ${a.manual_override ? 'overridden' : ''}">
                    <div class="d-flex justify-content-between">
                        <span class="badge bg-dark">${a.protocol.toUpperCase()}</span>
                        <div class="d-flex gap-2">
                            <button class="btn btn-link text-primary p-0" onclick="openEditModal('${a.name}')">
                                <i class="fas fa-edit"></i>
                            </button>
                            <button class="btn btn-link text-danger p-0" onclick="deleteAsset('${a.name}')">
                                <i class="fas fa-times"></i>
                            </button>
                        </div>
                    </div>
                    <div class="icon-container text-center my-2">
                        <i class="fas ${a.icon} fa-2x"></i>
                    </div>
                    <div class="text-center">
                        <h6 class="mb-0 fw-bold">${a.name}</h6>
                        <small class="text-muted d-block mb-2">${a.filename || a.name}.bin</small>
                        <h2 class="value-display my-2">${a.current_value.toFixed(2)}</h2>
                    </div>
                    <div class="d-flex gap-2 mt-2">
                        <button class="btn btn-sm btn-outline-warning w-100" onclick="sendOverride('${a.name}')">Inject</button>
                        <button class="btn btn-sm btn-outline-success w-100" onclick="sendRelease('${a.name}')">Auto</button>
                    </div>
                </div>
            </div>
        `).join('');
    } catch (err) {
        console.error("Dashboard sync error:", err);
    }
}

// 2. Open Edit Modal and Populate Fields
async function openEditModal(name) {
    try {
        const res = await fetch(`/api/assets/${name}`);
        if (!res.ok) throw new Error("Asset not found");
        const a = await res.json();

        // Map database values to Edit Modal fields
        document.getElementById('edit_name').value = a.name;
        document.getElementById('edit_type').value = a.type;
        document.getElementById('edit_filename').value = a.filename || a.name;
        document.getElementById('edit_protocol').value = a.protocol;
        document.getElementById('edit_addr').value = a.address;
        document.getElementById('edit_icon').value = a.icon;
        document.getElementById('edit_min').value = a.min_range;
        document.getElementById('edit_max').value = a.max_range;
        document.getElementById('edit_drift').value = a.drift_rate;

        // Show the Modal
        const editModal = new bootstrap.Modal(document.getElementById('editModal'));
        editModal.show();
    } catch (err) {
        alert("Error loading asset details: " + err.message);
    }
}

// 3. Save Changes (PUT Request)
async function saveAssetEdit() {
    const name = document.getElementById('edit_name').value;
    const data = {
        name: name,
        type: document.getElementById('edit_type').value,
        filename: document.getElementById('edit_filename').value,
        protocol: document.getElementById('edit_protocol').value,
        address: parseInt(document.getElementById('edit_addr').value),
        min_range: parseFloat(document.getElementById('edit_min').value),
        max_range: parseFloat(document.getElementById('edit_max').value),
        drift_rate: parseFloat(document.getElementById('edit_drift').value),
        icon: document.getElementById('edit_icon').value
    };

    try {
        const res = await fetch(`/api/assets/${name}`, {
            method: 'PUT',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify(data)
        });

        if (res.ok) {
            bootstrap.Modal.getInstance(document.getElementById('editModal')).hide();
            fetchAssets();
        } else {
            alert("Update failed. Check server logs.");
        }
    } catch (err) {
        console.error("Edit Error:", err);
    }
}

// 4. Provision New Asset (POST Request)
async function saveNewAsset() {
    const data = {
        name: document.getElementById('name').value,
        type: document.getElementById('type').value,
        filename: document.getElementById('filename').value, // Captured Filename
        protocol: document.getElementById('protocol').value,
        address: parseInt(document.getElementById('addr').value),
        min_range: parseFloat(document.getElementById('min').value),
        max_range: parseFloat(document.getElementById('max').value),
        drift_rate: parseFloat(document.getElementById('drift').value),
        icon: document.getElementById('icon').value
    };

    if (!data.name || isNaN(data.address)) {
        alert("Name and Address are mandatory.");
        return;
    }

    try {
        const res = await fetch('/api/assets', {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify(data)
        });

        if (res.ok) {
            bootstrap.Modal.getInstance(document.getElementById('addModal')).hide();
            fetchAssets();
            // Reset form
            document.getElementById('name').value = '';
            document.getElementById('filename').value = '';
        } else {
            const error = await res.json();
            alert("Deploy failed: " + JSON.stringify(error.detail));
        }
    } catch (err) {
        console.error("Network Error:", err);
    }
}

// 5. Delete, Override, and Release Functions
async function deleteAsset(name) {
    if (confirm(`Remove asset ${name}?`)) {
        await fetch(`/api/assets/${name}`, { method: 'DELETE' });
        fetchAssets();
    }
}

async function sendOverride(name) {
    const val = prompt(`Inject value for ${name}:`);
    if (val !== null) {
        await fetch(`/api/override/${name}?value=${val}`, { method: 'PUT' });
        fetchAssets();
    }
}

async function sendRelease(name) {
    await fetch(`/api/release/${name}`, { method: 'PUT' });
    fetchAssets();
}

// Polling interval
setInterval(fetchAssets, 1000);
fetchAssets();