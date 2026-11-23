local inp={active=false,steer=0.0,throt=0.0,brake=0.0,clutch=0.0,hb=0.0,gear=-1}
RegisterNUICallback('input',function(d,cb)
    inp=d
    if d.cmds then for _,c in ipairs(d.cmds) do ExecuteCommand(c) end end
    cb('ok')
end)

CreateThread(function()
    while true do
        local p=PlayerPedId()
        local v=GetVehiclePedIsIn(p,false)
        if v~=0 and GetPedInVehicleSeat(v,-1)==p then
            if inp.active then
                SetControlNormal(0,59,inp.steer)
                SetControlNormal(0,71,inp.throt)
                SetControlNormal(0,72,inp.brake)
                if inp.hb>0.05 then SetControlNormal(0,76,inp.hb) end
            end
            local vel,fwd=GetEntityVelocity(v),GetEntityForwardVector(v)
            local lat=(vel.x*fwd.y)+(vel.y*-fwd.x)
            SendNUIMessage({type='telemetry',active=true,speed=GetEntitySpeed(v),lateral=lat,steer=GetVehicleSteeringAngle(v),rpm=GetVehicleCurrentRpm(v),gear=GetVehicleCurrentGear(v),skid=GetVehicleWheelTractionVectorLength(v,0)+GetVehicleWheelTractionVectorLength(v,2),inAir=IsEntityInAir(v),bump=GetVehicleWheelSuspensionCompression(v,0)-GetVehicleWheelSuspensionCompression(v,1)})
            Wait(16)
        else
            SendNUIMessage({type='telemetry',active=false})
            Wait(250)
        end
    end
end)
