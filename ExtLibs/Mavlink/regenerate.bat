rd /s /q "mavlink"
python -m pymavlink.tools.mavgen --lang=csharp --wire-protocol=2.0 "message_definitions\aeronavics.xml"  
copy /y "mavlink\aeronavics\mavlink.cs" "Mavlink.cs"
pause