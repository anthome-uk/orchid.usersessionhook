#!/bin/bash

sudo cp orchid.usersessionhook.service /lib/systemd/user/
systemctl enable --now --user orchid.usersessionhook.service
