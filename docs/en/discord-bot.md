## Bot Token (Optional Configuration)

This project uses the Discord bot Token to connect to wss, allowing for the retrieval of error messages and full functionality, ensuring high availability of messages and other related issues.

```
1. Create an application
https://discord.com/developers/applications

2. Set application permissions (ensure you have read content permissions, refer to the screenshot)
[Bot] Settings -> Enable all

3. Add the application to the channel server (refer to the screenshot)

The client_id can be found on the application details page, which is the APPLICATION ID

https://discord.com/oauth2/authorize?client_id=xxx&permissions=8&scope=bot

4. Copy or reset the Bot Token to the configuration file
```

Set application permissions (ensure you have read content permissions, refer to the screenshot)

![Set application permissions](./docs/screenshots/gjODn5Nplq.png)

Add the application to the channel server (refer to the screenshot)

![Add application to channel server](./docs/screenshots/ItiWgaWIaX.png)