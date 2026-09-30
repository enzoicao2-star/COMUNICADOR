@echo off
netsh advfirewall firewall add rule name="Comunicador Teste" dir=in action=allow protocol=TCP localport=58933 profile=any >nul
netsh advfirewall firewall add rule name="Comunicador Teste (descoberta)" dir=in action=allow protocol=UDP localport=58934 profile=any >nul
exit /b 0