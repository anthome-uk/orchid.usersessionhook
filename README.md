## Orchid Session Hook
Sends an MQTT message when the user logs in or out

Credentials are read from `.mqtt.env` in the user's home directory:

  Username: <username>
  Password: <password>

Keep it private (`chmod 600`) — the file is read at login/logout by the service.

Installation:

  Linux - run install.sh

  Windows - TODO. Can't remember if I did this with gpedit or something else

