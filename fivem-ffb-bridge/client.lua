local lastNui = 0
function math.clamp(v,lo,hi) return math.min(hi,math.max(lo,v)) end
local inp = {active=false, steer=0.0, throt=0.0, brake=0.0, clutch=0.0, hb=0.0}
local gearMode = "auto" -- "auto", "sq", "h"
local targetGear = 1
local origHighGear = nil
local lastVeh = 0
local stalled = false
local uiOpen = false

local simCfg = {
    bitePoint = 0.35,
    stallOn = true,
    engBrake = 0.45,
    understeerFfb = 0.65,
    surfaceFfb = 1.0,
    noAbs = true, steerSpeed = 0.22
}

local function setMode(m)
    gearMode = m
    if m == "auto" and lastVeh ~= 0 and origHighGear then
        SetVehicleHighGear(lastVeh, origHighGear)
    end
end

RegisterCommand('gear_up', function()
    local v = GetVehiclePedIsIn(PlayerPedId(), false)
    if v ~= 0 then
        if gearMode == "auto" then setMode("sq") end
        local maxG = origHighGear or GetVehicleHighGear(v)
        targetGear = math.min(maxG, targetGear + 1)
    end
end, false)

RegisterCommand('gear_down', function()
    local v = GetVehiclePedIsIn(PlayerPedId(), false)
    if v ~= 0 then
        if gearMode == "auto" then setMode("sq") end
        targetGear = math.max(0, targetGear - 1)
    end
end, false)

RegisterCommand('gear_auto', function() setMode("auto") end, false)
RegisterCommand('gear_r', function() setMode("h"); targetGear = 0 end, false)
RegisterCommand('gear_n', function() setMode("h"); targetGear = -1 end, false)
for i = 1, 6 do
    RegisterCommand('gear_' .. i, function() setMode("h"); targetGear = i end, false)
end

local function toggleSimMenu()
    uiOpen = not uiOpen
    SetNuiFocus(uiOpen, uiOpen)
            SendNUIMessage({type = 'toggle_ui', open = uiOpen, cfg = simCfg, mode = gearMode})
end

RegisterCommand('simrig', toggleSimMenu, false)
RegisterKeyMapping('simrig', 'Avaa SimRig F10 -saatovalikko', 'keyboard', 'F10')

RegisterNUICallback('close_ui', function(_, cb)
    uiOpen = false
    SetNuiFocus(false, false)
    cb('ok')
end)

RegisterNUICallback('update_cfg', function(d, cb)
    if d.cfg then
        simCfg.bitePoint = tonumber(d.cfg.bitePoint) or simCfg.bitePoint
        simCfg.stallOn = d.cfg.stallOn ~= false
        simCfg.engBrake = tonumber(d.cfg.engBrake) or simCfg.engBrake
        simCfg.understeerFfb = tonumber(d.cfg.understeerFfb) or simCfg.understeerFfb
        simCfg.surfaceFfb = tonumber(d.cfg.surfaceFfb) or simCfg.surfaceFfb
        simCfg.noAbs = d.cfg.noAbs ~= false; simCfg.steerSpeed = tonumber(d.cfg.steerSpeed) or simCfg.steerSpeed
    end
    if d.mode then setMode(d.mode) end
    cb('ok')
end)

RegisterNUICallback('input', function(d, cb)
    inp = d
    if d.cmds then for _, c in ipairs(d.cmds) do ExecuteCommand(c) end end
    cb('ok')
end)

local function getSurfaceRoughness(mat)
    if mat == 1 or mat == 3 or mat == 4 or mat == 13 then return 0.05 end -- asfaltti/betoni
    if mat == 31 or mat == 32 or mat == 35 or mat == 36 then return 0.55 end -- sora/hiekka
    if mat == 46 or mat == 47 or mat == 48 then return 0.70 end -- ruoho/metsä
    return 0.25
end

CreateThread(function()
    while true do
        local p = PlayerPedId()
        local v = GetVehiclePedIsIn(p, false)
        if v ~= 0 and GetPedInVehicleSeat(v, -1) == p then
            if v ~= lastVeh then
                lastVeh = v
                origHighGear = GetVehicleHighGear(v)
                targetGear = math.max(1, GetVehicleCurrentGear(v))
                stalled = false
            end

            local speed = GetEntitySpeed(v)
            local fwd = GetEntityForwardVector(v)
            local vel = GetEntityVelocity(v)
            local fwdSpeed = (vel.x * fwd.x) + (vel.y * fwd.y)
            local latSpeed = (vel.x * fwd.y) + (vel.y * -fwd.x)
            local clutch = inp.clutch or 0.0
            local manualMode = (gearMode ~= "auto")

            if inp.active then
                local engage = 1.0 - math.clamp((clutch - 0.05) / math.max(0.1, 1.0 - simCfg.bitePoint), 0.0, 1.0)
                local st = math.clamp((inp.steer or 0.0) * (simCfg.steerSpeed or 0.22), -1.0, 1.0)
                local padSt = (math.abs(st) > 0.002) and ((st > 0 and 1 or -1) * (0.15 + math.abs(st) * 0.85)) or 0.0; SetControlNormal(0, 59, padSt)
                SetVehicleSteeringAngle(v, math.rad(-st * 36.0))
                SetControlNormal(0, 71, inp.throt)
                if inp.throt > 0.03 and not stalled and (gearMode == "auto" or targetGear > 0) and engage > 0.2 then ApplyForceToEntity(v, 1, 0.0, inp.throt * engage * 0.28, 0.0, 0.0, 0.0, 0.0, 0, true, true, true, false, true) end
                SetControlNormal(0, 72, inp.brake)
                SetControlNormal(0, 76, inp.hb > 0.08 and inp.hb or 0.0)
                if inp.hb < 0.08 then SetVehicleHandbrake(v, false) end
                SetVehicleUndriveable(v, false)

                -- Puremapiste & kytkin (0 = kiinni, 1 = pohjassa)
                if clutch > 0.15 and speed > 2.0 then
                    SetVehicleClutch(v, engage)
                end

                -- Sammuminen (Stall) ja uudelleenkäynnistys
                if stalled then
                    SetVehicleEngineOn(v, false, true, true)
                    SetControlNormal(0, 71, 0.0)
                    if (clutch > 0.75 or targetGear == -1 or gearMode == "auto") and inp.throt > 0.2 then
                        stalled = false
                        SetVehicleEngineOn(v, true, false, true)
                    end
                elseif simCfg.stallOn and manualMode and targetGear > 0 and speed < 2.2 and engage > 0.85 and inp.throt < 0.08 then
                    stalled = true
                    SetVehicleEngineOn(v, false, true, true)
                end

                if manualMode and not stalled then
                    if targetGear == -1 then
                        SetVehicleClutch(v, 0.0)
                        SetControlNormal(0, 71, 0.0)
                        if inp.throt > 0.05 then SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8)) end
                    elseif targetGear == 0 then
                        SetVehicleHighGear(v, 1)
                        SetVehicleCurrentGear(v, 0)
                        if inp.throt > 0.05 and engage > 0.2 then
                            SetControlNormal(0, 71, 0.0)
                            SetControlNormal(0, 72, inp.throt * engage)
                        else
                            SetControlNormal(0, 72, 0.0)
                        end
                    else
                        SetVehicleHighGear(v, targetGear)
                        SetVehicleCurrentGear(v, targetGear)

                        -- Mekaaninen kierrosluku ja moottorijarrutus
                        local gearMaxSpd = (targetGear * 11.5)
                        local mechRpm = math.clamp(0.2 + (math.abs(fwdSpeed) / gearMaxSpd) * 0.8, 0.2, 1.0)
                        if engage > 0.5 then
                            local curRpm = GetVehicleCurrentRpm(v)
                            SetVehicleCurrentRpm(v, (curRpm * (1.0 - engage)) + (mechRpm * engage))
                            if inp.throt < 0.05 and fwdSpeed > 3.0 and mechRpm > 0.45 then
                                local ebForce = -simCfg.engBrake * mechRpm * 3.2
                                ApplyForceToEntity(v, 1, 0.0, ebForce * 0.016, 0.0, 0.0, 0.0, 0.0, 0, true, true, true, false, true)
                            end
                        end

                        if GetEntitySpeedVector(v, true).y < 0.5 and inp.brake > 0.12 then
                            SetControlNormal(0, 72, 0.0)
                            SetVehicleBrake(v, true)
                        end
                    end
                end

                -- Kytkin pohjassa vapaa kierrosten nosto
                if engage < 0.15 and not stalled then
                    SetControlNormal(0, 71, 0.0)
                    SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8))
                end

                -- Jarrujen lukkiutuminen ilman ABS:ää
                if simCfg.noAbs and inp.brake > 0.88 and speed > 8.0 then
                    SetVehicleBrakeLights(v, true)
                    for w = 0, 3 do SetVehicleWheelRotationSpeed(v, w, 0.0) end
                end
            end
            local steerAngle = GetVehicleSteeringAngle(v)
            local bodySlip = (speed > 4.0) and math.atan(latSpeed, math.max(1.0, math.abs(fwdSpeed))) or 0.0
            local gripFactor = 1.0 - math.clamp((math.abs(steerAngle - bodySlip) - 0.18) * 1.8 * simCfg.understeerFfb, 0.0, 0.75)
            local effSteer = steerAngle * gripFactor
            local mat = GetVehicleWheelSurfaceMaterial(v, 0)
            local surfBump = (speed > 2.0) and (math.sin(GetGameTimer() * 0.06 * speed) * getSurfaceRoughness(mat) * simCfg.surfaceFfb * 0.35) or 0.0
            local suspBump = GetVehicleWheelSuspensionCompression(v, 0) - GetVehicleWheelSuspensionCompression(v, 1)
            if GetGameTimer() - lastNui >= 16 then
                lastNui = GetGameTimer()
                SendNUIMessage({
                    type = 'telemetry', active = true, speed = speed, lateral = latSpeed,
                    steer = effSteer * 4.5, rpm = GetVehicleCurrentRpm(v), gear = manualMode and targetGear or GetVehicleCurrentGear(v),
                    skid = GetVehicleWheelTractionVectorLength(v, 0) + GetVehicleWheelTractionVectorLength(v, 2),
                    inAir = IsEntityInAir(v), bump = (suspBump * 8.0) + (surfBump * 3.0),
                    ui = uiOpen, inp = inp, mode = gearMode, stalled = stalled
                })
            end
            if not uiOpen then SetNuiFocus(false, false) end
            Wait(0)
        else
            lastVeh = 0
            origHighGear = nil
            setMode("auto")
            SendNUIMessage({type = 'telemetry', active = false})
            Wait(250)
        end
    end
end)
