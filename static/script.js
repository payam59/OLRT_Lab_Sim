let socket;
let bbmdList = [];
let shouldReconnectSocket = true;
let reconnectTimer = null;

function renderAlarms(alarms) {
    const alarmList = document.getElementById('alarmList');
    if (!alarmList) return;

    if (alarms.length === 0) {
        alarmList.innerHTML = '<div class="list-group-item text-muted">No active alarms.</div>';
        return;
    }

    alarmList.innerHTML = alarms.map(a => `
        <div class="list-group-item list-group-item-danger">
            <div class="d-flex justify-content-between">
                <strong>${a.asset_name}</strong>
                <small>${new Date(a.created_at * 1000).toLocaleString()}</small>
            </div>
            <div>${a.message}</div>
        </div>
    `).join('');
}

function connectWebSocket() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    socket = new WebSocket(`${protocol}//${window.location.host}/ws`);

    socket.onmessage = function(event) {
        const assets = JSON.parse(event.data);
        renderAssets(assets);
        refreshAlarms();
    };

    socket.onclose = function() {
        if (!shouldReconnectSocket) return;
        if (reconnectTimer) clearTimeout(reconnectTimer);
        reconnectTimer = setTimeout(connectWebSocket, 2000);
    };
}

function refreshAlarms() {
    if (typeof window.fetchAlarms === 'function') {
        window.fetchAlarms();
    }
}

function renderBBMDs(bbmds) {
    const grid = document.getElementById('bbmdGrid');
    if (!grid) return;

    if (bbmds.length === 0) {
        grid.innerHTML = '<div class="col-12"><p class="text-muted">No BBMD devices configured. Click "Manage BBMD" to add one.</p></div>';
        return;
    }

    grid.innerHTML = bbmds.map(b => `
        <div class="col-md-6 col-lg-4 mb-3">
            <div class="card border-success shadow-sm">
                <div class="card-body">
                    <div class="d-flex justify-content-between align-items-start mb-2">
                        <div>
                            <h6 class="mb-0"><i class="fas fa-server text-success me-1"></i>${b.name}</h6>
                            <small class="text-muted">${b.description || 'No description'}</small>
                        </div>
                        <div class="d-flex gap-1">
                            <span class="badge ${b.enabled ? 'bg-success' : 'bg-secondary'}">${b.enabled ? 'Active' : 'Disabled'}</span>
                        </div>
                    </div>
                    <div class="small mt-2 mb-2">
                        <div><strong>Port:</strong> ${b.port}</div>
                        <div><strong>Device ID:</strong> ${b.device_id}</div>
                        <div><strong>IP:</strong> ${b.ip_address}</div>
                    </div>
                    <div class="d-flex gap-2">
                        <button class="btn btn-sm btn-outline-primary" data-bbmd-edit="${b.id}" onclick="window.editBBMD(${b.id})" type="button">
                            <i class="fas fa-edit"></i> Edit
                        </button>
                        <button class="btn btn-sm btn-outline-danger" data-bbmd-delete="${b.id}" onclick="window.deleteBBMD(${b.id})" type="button">
                            <i class="fas fa-trash"></i> Delete
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `).join('');
}

function renderAssets(assets) {
    const grid = document.getElementById('assetGrid');
    if (!grid) return;

    grid.innerHTML = assets.map(a => {
        const isDigital = a.sub_type === "Digital";
        const isActive = a.current_value >= 0.5;
        const statusText = isDigital ? (isActive ? 'ON' : 'OFF') : a.current_value.toFixed(2);
        const iconColorClass = isActive ? "text-warning" : "text-secondary";
        const inAlarm = a.alarm_state === 1;
        const cardBorderClass = inAlarm ? "border-danger border-3" : (a.manual_override ? "border-warning" : "");
        const cardBgClass = inAlarm ? "bg-danger-subtle" : (a.manual_override ? "overridden" : "");
        const bbmdBadge = a.bbmd_id ? `<span class="badge bg-success">BBMD #${a.bbmd_id}</span>` : '';
        const objectTypeBadge = `<span class="badge bg-info">${a.object_type || 'value'}</span>`;
        const modbusBadge = a.protocol === 'modbus'
            ? `<div class="small text-muted">Unit ${a.modbus_unit_id || 1} • ${a.modbus_register_type || 'holding'} @ ${a.address}</div>
               <div class="small text-muted">TCP ${a.modbus_ip || '0.0.0.0'}:${a.modbus_port || 5020}</div>`
            : '';
        const bacnetBadge = a.protocol === 'bacnet'
            ? `<div class="small text-muted">BACnet instance ${a.address}${a.bbmd_id ? ` • BBMD ${a.bbmd_id}` : ''}</div>`
            : '';

        return `
        <div class="col-md-4 col-lg-3 mb-4">
            <div class="card asset-card p-3 shadow-sm ${cardBorderClass} ${cardBgClass}">
                <div class="d-flex justify-content-between align-items-center mb-2">
                    <span class="badge bg-dark">${a.protocol.toUpperCase()}</span>
                    <div class="d-flex gap-2">
                        <button class="btn btn-link text-primary p-0" onclick="window.openEditModal('${a.name}')"><i class="fas fa-edit"></i></button>
                        <button class="btn btn-link text-danger p-0" onclick="window.deleteAsset('${a.name}')"><i class="fas fa-times"></i></button>
                    </div>
                </div>
                ${inAlarm ? `<div class="alert alert-danger py-1 px-2 mb-2 small"><i class="fas fa-exclamation-triangle me-1"></i>${a.alarm_message}</div>` : ''}
                <div class="icon-container text-center my-2">
                    <i class="fas ${a.icon} fa-3x ${inAlarm ? 'text-danger' : iconColorClass}" style="transition: color 0.3s ease;"></i>
                </div>
                <div class="text-center">
                    <h6 class="mb-0 fw-bold">${a.name}</h6>
                    <small class="text-muted d-block mb-1">${isDigital ? (a.is_normally_open ? 'N.O.' : 'N.C.') : 'Analog'}</small>
                    <div class="mb-1">${bbmdBadge} ${objectTypeBadge}</div>
                    ${bacnetBadge}
                    ${modbusBadge}
                    <h2 class="value-display my-2 ${inAlarm ? 'text-danger fw-bold' : (isActive ? 'text-success' : '')}">${statusText}</h2>
                    ${!isDigital ? `<small class="text-muted">Range: ${a.min_range} - ${a.max_range}</small>` : ''}
                </div>
                <div class="d-flex gap-2 mt-2">
                    ${isDigital ?
                        `<button class="btn btn-sm ${isActive ? 'btn-danger' : 'btn-success'} w-100" onclick="window.toggleDigital('${a.name}', ${a.current_value})">
                            ${isActive ? 'Turn OFF' : 'Turn ON'}
                        </button>` :
                        `<button class="btn btn-sm btn-outline-warning w-100" onclick="window.sendOverride('${a.name}')">Inject</button>`
                    }
                    <button class="btn btn-sm btn-outline-secondary w-100" onclick="window.sendRelease('${a.name}')">Auto</button>
                </div>
            </div>
        </div>`;
    }).join('');
}

window.fetchBBMDs = async function() {
    try {
        const response = await fetch('/api/bbmd');
        bbmdList = await response.json();
        renderBBMDs(bbmdList);
        updateBBMDSelects();
        updateBBMDList();
    } catch (error) {
        console.error('Error fetching BBMDs:', error);
    }
};

window.fetchAssets = async function() {
    const response = await fetch('/api/assets');
    const assets = await response.json();
    renderAssets(assets);
    window.fetchAlarms();
};

window.fetchAlarms = async function() {
    try {
        const response = await fetch('/api/alarms?active_only=1');
        if (!response.ok) {
            console.error('Failed to fetch alarms:', response.status);
            return;
        }
        const alarms = await response.json();
        renderAlarms(alarms);
    } catch (error) {
        console.error('Failed to fetch alarms:', error);
    }
};

function updateBBMDSelects() {
    const selects = ['bbmd_select', 'edit_bbmd_select'];
    selects.forEach(selectId => {
        const select = document.getElementById(selectId);
        if (select) {
            if (bbmdList.length === 0) {
                select.innerHTML = '<option value="">⚠ No BBMD configured - Add one first!</option>';
            } else {
                select.innerHTML = '<option value="">Select BBMD (required for BACnet)</option>' +
                    bbmdList.map(b => `<option value="${b.id}">${b.name} - Port:${b.port} DevID:${b.device_id}</option>`).join('');
            }
        }
    });
}

function updateBBMDList() {
    const list = document.getElementById('bbmdList');
    if (!list) return;

    if (bbmdList.length === 0) {
        list.innerHTML = '<p class="text-muted">No BBMD devices configured.</p>';
        return;
    }

    list.innerHTML = bbmdList.map(b => `
        <div class="border rounded p-2 mb-2 ${b.enabled ? 'border-success' : 'border-secondary'}">
            <div class="d-flex justify-content-between align-items-center">
                <div>
                    <strong>${b.name}</strong> <small class="text-muted">${b.description}</small><br>
                    <small>Port: ${b.port} | Device ID: ${b.device_id} | IP: ${b.ip_address}</small>
                </div>
                <button class="btn btn-sm btn-danger" data-bbmd-delete="${b.id}" onclick="window.deleteBBMD(${b.id})" type="button">
                    <i class="fas fa-trash"></i>
                </button>
            </div>
        </div>
    `).join('');
}

window.showAddBBMDForm = function() {
    document.getElementById('bbmdFormTitle').textContent = 'New BBMD Configuration';
    document.getElementById('bbmd_edit_id').value = '';
    document.getElementById('addBBMDForm').style.display = 'block';
};

window.editBBMD = function(id) {
    const bbmd = bbmdList.find(b => b.id === id);
    if (!bbmd) return;

    const bbmdModalElement = document.getElementById('bbmdModal');
    if (bbmdModalElement) {
        bootstrap.Modal.getOrCreateInstance(bbmdModalElement).show();
    }

    document.getElementById('bbmdFormTitle').textContent = 'Edit BBMD Configuration';
    document.getElementById('bbmd_edit_id').value = id;
    document.getElementById('bbmd_name').value = bbmd.name;
    document.getElementById('bbmd_desc').value = bbmd.description || '';
    document.getElementById('bbmd_port').value = bbmd.port;
    document.getElementById('bbmd_device_id').value = bbmd.device_id;
    document.getElementById('bbmd_ip').value = bbmd.ip_address;
    document.getElementById('addBBMDForm').style.display = 'block';

    // Scroll to form after modal is visible
    setTimeout(() => {
        document.getElementById('addBBMDForm').scrollIntoView({ behavior: 'smooth' });
    }, 150);
};

window.cancelBBMDForm = function() {
    document.getElementById('addBBMDForm').style.display = 'none';
    document.getElementById('bbmd_edit_id').value = '';
    document.getElementById('bbmd_name').value = '';
    document.getElementById('bbmd_desc').value = '';
    document.getElementById('bbmd_port').value = '47808';
    document.getElementById('bbmd_device_id').value = '1234';
    document.getElementById('bbmd_ip').value = '0.0.0.0';
};

window.saveBBMD = async function() {
    const editId = document.getElementById('bbmd_edit_id').value;
    const data = {
        name: document.getElementById('bbmd_name').value,
        description: document.getElementById('bbmd_desc').value,
        port: parseInt(document.getElementById('bbmd_port').value),
        device_id: parseInt(document.getElementById('bbmd_device_id').value),
        ip_address: document.getElementById('bbmd_ip').value,
        enabled: 1
    };

    const isEdit = editId !== '';
    const url = isEdit ? `/api/bbmd/${editId}` : '/api/bbmd';
    const method = isEdit ? 'PUT' : 'POST';

    const res = await fetch(url, {
        method: method,
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify(data)
    });

    if (res.ok) {
        window.cancelBBMDForm();
        window.fetchBBMDs();
    } else {
        alert(`Failed to ${isEdit ? 'update' : 'create'} BBMD: ` + (await res.text()));
    }
};

window.deleteBBMD = async function(id) {
    if (confirm('Delete this BBMD? Associated assets will be unlinked.')) {
        await fetch(`/api/bbmd/${id}`, { method: 'DELETE' });
        window.fetchBBMDs();
    }
};

window.saveNewAsset = async function() {
    const protocol = document.getElementById('protocol').value;
    const isBacnet = protocol === 'bacnet';
    const bbmdValue = document.getElementById('bbmd_select').value;
    const assetName = (document.getElementById('name').value || '').trim();

    if (!assetName) {
        alert('Asset Name is required.');
        return;
    }

    // Get address and icon based on protocol
    const address = isBacnet ?
        parseInt(document.getElementById('addr').value) :
        parseInt(document.getElementById('modbus_addr').value);
    const icon = isBacnet ?
        document.getElementById('icon').value :
        document.getElementById('modbus_icon').value;

    const data = {
        name: assetName,
        type: document.getElementById('type').value,
        sub_type: document.getElementById('sub_type').value,
        protocol: protocol,
        address: address,
        min_range: parseFloat(document.getElementById('min').value) || 0,
        max_range: parseFloat(document.getElementById('max').value) || 100,
        drift_rate: parseFloat(document.getElementById('drift').value) || 0,
        icon: icon,
        bacnet_port: parseInt(document.getElementById('bac_port').value) || 47808,
        bacnet_device_id: parseInt(document.getElementById('bac_id').value) || 1234,
        is_normally_open: parseInt(document.getElementById('logic_state').value),
        change_probability: parseFloat(document.getElementById('prob').value) || 0,
        change_interval: parseInt(document.getElementById('interval').value) || 15,
        bbmd_id: isBacnet && bbmdValue ? parseInt(bbmdValue) : null,
        object_type: isBacnet ? document.getElementById('object_type').value : 'value',
        bacnet_properties: isBacnet ? (document.getElementById('bacnet_properties').value || '{}') : '{}',
        modbus_unit_id: parseInt(document.getElementById('modbus_unit_id').value) || 1,
        modbus_register_type: document.getElementById('modbus_register_type').value || 'holding',
        modbus_ip: document.getElementById('modbus_ip').value || '0.0.0.0',
        modbus_port: parseInt(document.getElementById('modbus_port').value) || 5020,
        modbus_zero_based: document.getElementById('modbus_zero_based').checked ? 1 : 0,
        modbus_alarm_address: (() => {
            const raw = document.getElementById('modbus_alarm_address').value;
            return raw === '' ? null : parseInt(raw);
        })(),
        modbus_alarm_bit: parseInt(document.getElementById('modbus_alarm_bit').value) || 0
    };

    if (isBacnet && !data.bbmd_id) {
        alert('BACnet assets must be mapped to a BBMD.');
        return;
    }
    if (!isBacnet && !data.modbus_ip) {
        alert('Modbus IP is required for Modbus assets.');
        return;
    }

    const res = await fetch('/api/assets', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(data) });
    if (res.ok) {
        bootstrap.Modal.getInstance(document.getElementById('addModal')).hide();
        window.fetchAssets();
    } else {
        alert('Failed to create asset: ' + (await res.text()));
    }
};

window.openEditModal = async function(name) {
    const res = await fetch(`/api/assets/${name}`);
    const a = await res.json();

    document.getElementById('edit_name').value = a.name;
    document.getElementById('edit_type').value = a.type;
    document.getElementById('edit_sub_type').value = a.sub_type;
    document.getElementById('edit_protocol').value = a.protocol;
    document.getElementById('edit_min').value = a.min_range;
    document.getElementById('edit_max').value = a.max_range;
    document.getElementById('edit_drift').value = a.drift_rate;
    document.getElementById('edit_logic_state').value = a.is_normally_open;
    document.getElementById('edit_prob').value = a.change_probability;
    document.getElementById('edit_interval').value = a.change_interval;
    document.getElementById('edit_bac_port').value = a.bacnet_port;
    document.getElementById('edit_bac_id').value = a.bacnet_device_id;
    document.getElementById('edit_object_type').value = a.object_type || 'value';
    document.getElementById('edit_bbmd_select').value = a.bbmd_id || '';
    document.getElementById('edit_bacnet_properties').value = a.bacnet_properties || '{}';

    // Set address and icon based on protocol
    const isBacnet = a.protocol === 'bacnet';
    if (isBacnet) {
        document.getElementById('edit_addr').value = a.address;
        document.getElementById('edit_icon').value = a.icon;
    } else {
        document.getElementById('edit_modbus_addr').value = a.address;
        document.getElementById('edit_modbus_icon').value = a.icon;
        document.getElementById('edit_modbus_unit_id').value = a.modbus_unit_id || 1;
        document.getElementById('edit_modbus_register_type').value = a.modbus_register_type || 'holding';
        document.getElementById('edit_modbus_ip').value = a.modbus_ip || '0.0.0.0';
        document.getElementById('edit_modbus_port').value = a.modbus_port || 5020;
        document.getElementById('edit_modbus_zero_based').checked = (a.modbus_zero_based ?? 1) === 1;
        document.getElementById('edit_modbus_alarm_address').value = a.modbus_alarm_address ?? '';
        document.getElementById('edit_modbus_alarm_bit').value = a.modbus_alarm_bit ?? 0;
    }

    window.toggleFields('edit_');
    window.toggleProtocolFields('edit_');
    new bootstrap.Modal(document.getElementById('editModal')).show();
};

window.saveAssetEdit = async function() {
    const name = document.getElementById('edit_name').value;
    const protocol = document.getElementById('edit_protocol').value;
    const isBacnet = protocol === 'bacnet';
    const bbmdValue = document.getElementById('edit_bbmd_select').value;

    // Get address and icon based on protocol
    const address = isBacnet ?
        parseInt(document.getElementById('edit_addr').value) :
        parseInt(document.getElementById('edit_modbus_addr').value);
    const icon = isBacnet ?
        document.getElementById('edit_icon').value :
        document.getElementById('edit_modbus_icon').value;

    const data = {
        name: name,
        type: document.getElementById('edit_type').value,
        sub_type: document.getElementById('edit_sub_type').value,
        protocol: protocol,
        address: address,
        min_range: parseFloat(document.getElementById('edit_min').value) || 0,
        max_range: parseFloat(document.getElementById('edit_max').value) || 100,
        drift_rate: parseFloat(document.getElementById('edit_drift').value) || 0,
        icon: icon,
        bacnet_port: parseInt(document.getElementById('edit_bac_port').value) || 47808,
        bacnet_device_id: parseInt(document.getElementById('edit_bac_id').value) || 1234,
        is_normally_open: parseInt(document.getElementById('edit_logic_state').value),
        change_probability: parseFloat(document.getElementById('edit_prob').value) || 0,
        change_interval: parseInt(document.getElementById('edit_interval').value) || 15,
        bbmd_id: isBacnet && bbmdValue ? parseInt(bbmdValue) : null,
        object_type: isBacnet ? document.getElementById('edit_object_type').value : 'value',
        bacnet_properties: isBacnet ? (document.getElementById('edit_bacnet_properties').value || '{}') : '{}',
        modbus_unit_id: parseInt(document.getElementById('edit_modbus_unit_id').value) || 1,
        modbus_register_type: document.getElementById('edit_modbus_register_type').value || 'holding',
        modbus_ip: document.getElementById('edit_modbus_ip').value || '0.0.0.0',
        modbus_port: parseInt(document.getElementById('edit_modbus_port').value) || 5020,
        modbus_zero_based: document.getElementById('edit_modbus_zero_based').checked ? 1 : 0,
        modbus_alarm_address: (() => {
            const raw = document.getElementById('edit_modbus_alarm_address').value;
            return raw === '' ? null : parseInt(raw);
        })(),
        modbus_alarm_bit: parseInt(document.getElementById('edit_modbus_alarm_bit').value) || 0
    };

    if (isBacnet && !data.bbmd_id) {
        alert('BACnet assets must be mapped to a BBMD.');
        return;
    }

    const res = await fetch(`/api/assets/${name}`, {
        method: 'PUT',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify(data)
    });

    if (res.ok) {
        bootstrap.Modal.getInstance(document.getElementById('editModal')).hide();
        window.fetchAssets();
    } else {
        alert('Failed to update asset: ' + (await res.text()));
    }
};

window.deleteAsset = async function(name) {
    if (confirm(`Remove ${name}?`)) {
        await fetch(`/api/assets/${name}`, { method: 'DELETE' });
        window.fetchAssets();
    }
};

window.toggleDigital = async function(name, currentVal) {
    const newVal = currentVal >= 0.5 ? 0.0 : 1.0;
    await fetch(`/api/override/${name}?value=${newVal}`, { method: 'PUT' });
    window.fetchAssets();
};

window.sendRelease = async function(name) {
    await fetch(`/api/release/${name}`, { method: 'PUT' });
    window.fetchAssets();
};

window.sendOverride = async function(name) {
    const val = prompt("Manual Analog Value:");
    if (val !== null) {
        await fetch(`/api/override/${name}?value=${val}`, { method: 'PUT' });
        window.fetchAssets();
    }
};

window.toggleFields = function(prefix = '') {
    const subType = document.getElementById(prefix + 'sub_type').value;
    const isDigital = subType === 'Digital';
    document.getElementById(prefix + 'analog_fields').style.display = isDigital ? 'none' : 'block';
    document.getElementById(prefix + 'digital_fields').style.display = isDigital ? 'block' : 'none';
};

window.toggleProtocolFields = function(prefix = '') {
    const protocol = document.getElementById(prefix + 'protocol').value;
    const isBacnet = protocol === 'bacnet';

    // Toggle config sections
    const bacnetSection = document.getElementById(prefix + 'bacnet_config_section');
    const modbusSection = document.getElementById(prefix + 'modbus_config_section');
    const objectTypeContainer = document.getElementById(prefix + 'object_type_container');

    if (bacnetSection) bacnetSection.style.display = isBacnet ? 'block' : 'none';
    if (modbusSection) modbusSection.style.display = isBacnet ? 'none' : 'block';
    if (objectTypeContainer) objectTypeContainer.style.display = isBacnet ? 'block' : 'none';
};

document.addEventListener('DOMContentLoaded', () => {
    const bbmdGrid = document.getElementById('bbmdGrid');
    if (bbmdGrid) {
        bbmdGrid.addEventListener('click', (event) => {
            const target = event.target.closest('button');
            if (!target) return;
            const editId = target.getAttribute('data-bbmd-edit');
            const deleteId = target.getAttribute('data-bbmd-delete');
            if (editId) window.editBBMD(parseInt(editId));
            if (deleteId) window.deleteBBMD(parseInt(deleteId));
        });
    }

    const bbmdList = document.getElementById('bbmdList');
    if (bbmdList) {
        bbmdList.addEventListener('click', (event) => {
            const target = event.target.closest('button');
            if (!target) return;
            const deleteId = target.getAttribute('data-bbmd-delete');
            if (deleteId) window.deleteBBMD(parseInt(deleteId));
        });
    }

    window.fetchBBMDs();
    window.fetchAssets();
    refreshAlarms();
    setInterval(window.fetchAssets, 5000);
    setInterval(refreshAlarms, 5000);
    connectWebSocket();
});

window.addEventListener('beforeunload', () => {
    shouldReconnectSocket = false;
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
    }
    if (socket && socket.readyState === WebSocket.OPEN) {
        socket.close();
    }
});
