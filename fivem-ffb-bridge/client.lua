local inp = {active=false, steer=0.0, throt=0.0, brake=0.0, clutch=0.0, hb=0.0}
local manualMode, targetGear, origHighGear, lastVeh, uiOpen = false, 1, nil, 0, false
RegisterCommand("ffb", function() uiOpen = not uiOpen; SetNuiFocus(uiOpen, uiOpen); SendNUIMessage({type="ui", open=uiOpen}) end, false)
RegisterKeyMapping("ffb", "Avaa SimRig FFB -säädöt", "keyboard", "F10")
RegisterNUICallback("close", function(_, cb) uiOpen = false; SetNuiFocus(false, false); cb("ok") end)
RegisterCommand("gear_up", function() local v = GetVehiclePedIsIn(PlayerPedId(), false); if v ~= 0 then manualMode = true; targetGear = math.min(origHighGear or GetVehicleHighGear(v), targetGear + 1) end end, false)
RegisterCommand("gear_down", function() local v = GetVehiclePedIsIn(PlayerPedId(), false); if v ~= 0 then manualMode = true; targetGear = math.max(0, targetGear - 1) end end, false)
RegisterCommand("gear_auto", function() manualMode = false; local v = GetVehiclePedIsIn(PlayerPedId(), false); if v ~= 0 and origHighGear then SetVehicleHighGear(v, origHighGear) end end, false)
RegisterCommand("gear_r", function() manualMode = true; targetGear = 0 end, false)
RegisterCommand("gear_n", function() manualMode = true; targetGear = -1 end, false)
for i = 1, 6 do RegisterCommand("gear_" .. i, function() manualMode = true; targetGear = i end, false) end
RegisterNUICallback("input", function(d, cb) inp = d; if d.cmds then for _, c in ipairs(d.cmds) do ExecuteCommand(c) end end; cb("ok") end)
CreateThread(function()
    while true do
        local p = PlayerPedId(); local v = GetVehiclePedIsIn(p, false)
        if v ~= 0 and GetPedInVehicleSeat(v, -1) == p then
            if v ~= lastVeh then lastVeh = v; origHighGear = GetVehicleHighGear(v); manualMode = false end
            if inp.active then
                local maxAng = GetVehicleHandlingFloat(v, "CHandlingData", "fSteeringLock")
                if not maxAng or maxAng <= 0.0 then maxAng = 35.0 end
                SetVehicleSteeringAngle(v, math.rad(-inp.steer * maxAng))
                SetControlNormal(0, 71, inp.throt); SetControlNormal(0, 72, inp.brake)
                if (inp.hb or 0.0) > 0.05 then SetControlNormal(0, 76, inp.hb) end
                local clutch = inp.clutch or 0.0
                if manualMode then
                    if targetGear == -1 then SetVehicleClutch(v, 0.0); SetControlNormal(0, 71, 0.0); if inp.throt > 0.05 then SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8)) end
                    elseif targetGear == 0 then if GetVehicleCurrentGear(v) ~= 0 then SetVehicleCurrentGear(v, 0) end; if inp.throt > 0.05 and clutch < 0.5 then SetControlNormal(0, 71, 0.0); SetControlNormal(0, 72, inp.throt) end
                    else SetVehicleHighGear(v, math.max(targetGear, 2)); if GetVehicleCurrentGear(v) ~= targetGear then SetVehicleCurrentGear(v, targetGear) end end
                elseif origHighGear then SetVehicleHighGear(v, origHighGear) end
                if clutch > 0.1 then SetVehicleClutch(v, math.max(0.0, 1.0 - clutch)); if clutch > 0.75 then SetControlNormal(0, 71, 0.0); SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8)) end
                elseif not manualMode or targetGear ~= -1 then SetVehicleClutch(v, 1.0) end
            end
            local vel, fwd = GetEntityVelocity(v), GetEntityForwardVector(v)
            SendNUIMessage({type="telemetry", active=true, speed=GetEntitySpeed(v), lateral=(vel.x*fwd.y)+(vel.y*-fwd.x), steer=GetVehicleSteeringAngle(v), rpm=GetVehicleCurrentRpm(v), gear=GetVehicleCurrentGear(v), skid=GetVehicleWheelTractionVectorLength(v,0)+GetVehicleWheelTractionVectorLength(v,2), inAir=IsEntityInAir(v), bump=GetVehicleWheelSuspensionCompression(v,0)-GetVehicleWheelSuspensionCompression(v,1)})
            Wait(16)
        else lastVeh = 0; manualMode = false; SendNUIMessage({type="telemetry", active=false}); Wait(250) end
    end
end)
