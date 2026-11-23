local inp = {active=false, steer=0.0, throt=0.0, brake=0.0, clutch=0.0, hb=0.0}
local manualMode = false
local targetGear = 1

RegisterCommand('gear_up', function()
    local v = GetVehiclePedIsIn(PlayerPedId(), false)
    if v ~= 0 then manualMode = true; targetGear = math.min(GetVehicleHighGear(v), targetGear + 1) end
end, false)

RegisterCommand('gear_down', function()
    local v = GetVehiclePedIsIn(PlayerPedId(), false)
    if v ~= 0 then manualMode = true; targetGear = math.max(0, targetGear - 1) end
end, false)

RegisterCommand('gear_auto', function() manualMode = false end, false)
RegisterCommand('gear_r', function() manualMode = true; targetGear = 0 end, false)
RegisterCommand('gear_n', function() manualMode = true; targetGear = -1 end, false)
for i = 1, 6 do RegisterCommand('gear_' .. i, function() manualMode = true; targetGear = i end, false) end

RegisterNUICallback('input', function(d, cb)
    inp = d
    if d.cmds then for _, c in ipairs(d.cmds) do ExecuteCommand(c) end end
    cb('ok')
end)

CreateThread(function()
    while true do
        local p = PlayerPedId()
        local v = GetVehiclePedIsIn(p, false)
        if v ~= 0 and GetPedInVehicleSeat(v, -1) == p then
            if inp.active then
                SetControlNormal(0, 59, inp.steer)
                SetControlNormal(0, 71, inp.throt)
                SetControlNormal(0, 72, inp.brake)
                if inp.hb > 0.05 then SetControlNormal(0, 76, inp.hb) end
                local clutch = inp.clutch or 0.0
                if manualMode then
                    if targetGear == -1 then
                        SetVehicleClutch(v, 0.0)
                        SetControlNormal(0, 71, 0.0)
                        if inp.throt > 0.05 then SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8)) end
                    elseif targetGear == 0 then
                        SetVehicleHighGear(v, 1)
                        SetVehicleCurrentGear(v, 0)
                        if inp.throt > 0.05 and clutch < 0.5 then
                            SetControlNormal(0, 71, 0.0)
                            SetControlNormal(0, 72, inp.throt)
                        else
                            SetControlNormal(0, 72, 0.0)
                        end
                    else
                        SetVehicleHighGear(v, targetGear)
                        SetVehicleCurrentGear(v, targetGear)
                        if GetEntitySpeedVector(v, true).y < 0.5 and inp.brake > 0.05 then
                            SetControlNormal(0, 72, 0.0)
                            SetVehicleBrake(v, true)
                        end
                    end
                end
                if clutch > 0.1 then
                    SetVehicleClutch(v, math.max(0.0, 1.0 - clutch))
                    if clutch > 0.75 then
                        SetControlNormal(0, 71, 0.0)
                        SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8))
                    end
                end
            end
            local vel, fwd = GetEntityVelocity(v), GetEntityForwardVector(v)
            local lat = (vel.x * fwd.y) + (vel.y * -fwd.x)
            SendNUIMessage({
                type='telemetry', active=true, speed=GetEntitySpeed(v), lateral=lat,
                steer=GetVehicleSteeringAngle(v), rpm=GetVehicleCurrentRpm(v), gear=GetVehicleCurrentGear(v),
                skid=GetVehicleWheelTractionVectorLength(v,0)+GetVehicleWheelTractionVectorLength(v,2),
                inAir=IsEntityInAir(v), bump=GetVehicleWheelSuspensionCompression(v,0)-GetVehicleWheelSuspensionCompression(v,1)
            })
            Wait(16)
        else
            manualMode = false
            SendNUIMessage({type='telemetry', active=false})
            Wait(250)
        end
    end
end)
