#!/bin/bash

dotnet build -c Release
sudo cp orchid.usersessionhook.service /lib/systemd/user/
sudo sed  's:~PATH_ROOT~:'$(pwd)':g' -i /lib/systemd/user/orchid.usersessionhook.service
systemctl enable --now --user orchid.usersessionhook.service
