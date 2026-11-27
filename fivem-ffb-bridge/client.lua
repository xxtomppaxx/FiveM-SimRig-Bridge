SetNuiFocus(false, false)
local steerRatio = 1.95
RegisterNUICallback("steer_cfg", function(d, cb) if d.deg then steerRatio = 900.0 / math.max(180.0, tonumber(d.deg)) end cb("ok") end)
local inp = {active=false, steer=0.0, throt=0.0, brake=0.0, clutch=0.0, hb=0.0}
local manualMode, targetGear, origHighGear, lastVeh, uiOpen = false, 1, nil, 0, false
RegisterCommand("ffb", function() uiOpen = not uiOpen; SetNuiFocus(uiOpen, uiOpen); SetNuiFocusKeepInput(uiOpen); SendNUIMessage({type="ui", open=uiOpen}) end, false)
RegisterKeyMapping("ffb", "Avaa SimRig FFB -säädöt", "keyboard", "F10")
RegisterNUICallback("gmode",function(d,cb) manualMode=(d.mode~="auto");cb("ok") end)
RegisterNUICallback("close", function(_, cb) uiOpen = false; SetNuiFocus(false, false); SetNuiFocusKeepInput(false); cb("ok") end)
RegisterCommand("gear_up", function() local v = GetVehiclePedIsIn(PlayerPedId(), false); if v ~= 0 then manualMode = true; targetGear = math.min(origHighGear or GetVehicleHighGear(v), targetGear + 1) end end, false)
RegisterCommand("gear_down", function() local v = GetVehiclePedIsIn(PlayerPedId(), false); if v ~= 0 then manualMode = true; targetGear = math.max(0, targetGear - 1) end end, false)
RegisterCommand("gear_auto", function() manualMode = false; local v = GetVehiclePedIsIn(PlayerPedId(), false); if v ~= 0 and origHighGear then SetVehicleHighGear(v, origHighGear) end end, false)
RegisterCommand("gear_r", function() manualMode = true; targetGear = 0 end, false)
RegisterCommand("gear_n", function() manualMode = true; targetGear = -1 end, false)
for i = 1, 6 do RegisterCommand("gear_" .. i, function() manualMode = true; targetGear = i end, false) end
RegisterNUICallback("input", function(d, cb) inp = d; if d.cmds then for _, c in ipairs(d.cmds) do ExecuteCommand(c) end end; cb("ok") end)
local lastTel=0
CreateThread(function()
    while true do
        local p = PlayerPedId(); local v = GetVehiclePedIsIn(p, false)
        if v ~= 0 and GetPedInVehicleSeat(v, -1) == p then
            if v ~= lastVeh then lastVeh = v; origHighGear = GetVehicleHighGear(v); manualMode = false end
            if inp.active then
                
                local maxAng = 35.0
                local st=math.max(-1.0,math.min(1.0,inp.steer*1.35)); if math.abs(st)>0.003 then local s=math.abs(st); local shaped=s^0.7; local padSt=(st>0 and 1 or -1)*(0.16+shaped*0.84); SetControlNormal(0,59,padSt) end;
                if inp.throt>0.02 then SetControlNormal(0,71,inp.throt); if IsVehicleOnAllWheels(v) and (inp.clutch or 0)<0.7 and targetGear~=-1 then local d=(targetGear==0) and -1.0 or 1.0; ApplyForceToEntityCenterOfMass(v,1,0.0,d*inp.throt*0.55,-0.25,false,true,true,false) end end; if inp.brake>0.02 then if manualMode and targetGear~=0 and GetEntitySpeed(v)<0.5 then SetVehicleHandbrake(v,true) else SetControlNormal(0,72,inp.brake) end end; if (inp.hb or 0.0) <= 0.05 then SetVehicleHandbrake(v, false) end
                if (inp.hb or 0.0) > 0.05 then SetControlNormal(0, 76, inp.hb) end
                local clutch = inp.clutch or 0.0
                if manualMode then
                    if targetGear == -1 then SetVehicleClutch(v, 0.0); if inp.throt > 0.05 then SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8)) end
                    elseif targetGear == 0 then if GetVehicleCurrentGear(v) ~= 0 then SetVehicleCurrentGear(v, 0) end; if inp.throt > 0.05 and clutch < 0.5 then SetControlNormal(0, 72, inp.throt) end
                    else local caps={15.0,26.0,39.0,52.0,65.0,999.0}; SetEntityMaxSpeed(v, caps[targetGear] or 999.0) end
                else SetEntityMaxSpeed(v, 999.0) end
                if clutch > 0.1 then SetVehicleClutch(v, math.max(0.0, 1.0 - clutch)); if clutch > 0.75 then SetVehicleCurrentRpm(v, 0.2 + (inp.throt * 0.8)) end
                end
            end
            local now=GetGameTimer(); if now-lastTel>=16 then lastTel=now; local vel, fwd = GetEntityVelocity(v), GetEntityForwardVector(v)
            SendNUIMessage({type="telemetry", active=true, speed=GetEntitySpeed(v), lateral=(vel.x*fwd.y)+(vel.y*-fwd.x), steer=GetVehicleSteeringAngle(v), rpm=GetVehicleCurrentRpm(v), gear=GetVehicleCurrentGear(v), skid=GetVehicleWheelTractionVectorLength(v,0)+GetVehicleWheelTractionVectorLength(v,2), inAir=IsEntityInAir(v), bump=GetVehicleWheelSuspensionCompression(v,0)-GetVehicleWheelSuspensionCompression(v,1)}) end
            Wait(0)
        else lastVeh = 0; manualMode = false; SendNUIMessage({type="telemetry", active=false}); Wait(250) end
    end
end)
