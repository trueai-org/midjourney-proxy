# API Interface Description

`http://ip:port/mj` API documentation already exists, this is just a supplement.

## 1. Data Structure

### Task
| Field | Type | Example | Description |
|:-----:|:----:|:----|:----|
| id | string | 1689231405853400 | Task ID |
| action | string | IMAGINE | Task type: IMAGINE (drawing), UPSCALE (enlarge selected), VARIATION (transform selected), REROLL (re-execute), DESCRIBE (image to text), BLEND (image mixing) |
| status | string | SUCCESS | Task status: NOT_START (not started), SUBMITTED (submitted for processing), IN_PROGRESS (in progress), FAILURE (failed), SUCCESS (successful) |
| prompt | string | Cat | Prompt word |
| promptEn | string | Cat | English prompt word |
| description | string | /imagine Cat | Task description |
| submitTime | number | 1689231405854 | Submission time |
| startTime | number | 1689231442755 | Start execution time |
| finishTime | number | 1689231544312 | End time |
| progress | string | 100% | Task progress |
| imageUrl | string | https://cdn.discordapp.com/attachments/xxx/xxx/xxxx.png | URL of the generated image, has value when successful or in progress, may be png or webp |
| failReason | string | [Invalid parameter] Invalid value | Failure reason, has value when failed |
| properties | object | {"finalPrompt": "Cat"} | Extended properties of the task, for internal system use |


## 2. Task Submission Response
- code=1: Submission successful, result is the task ID
    ```json
    {
      "code": 1,
      "description": "Success",
      "result": "8498455807619990",
      "properties": {
          "discordInstanceId": "1118138338562560102"
      }
    }
    ```
- code=21: Task already exists, may occur during U
    ```json
    {
        "code": 21,
        "description": "Task already exists",
        "result": "0741798445574458",
        "properties": {
            "status": "SUCCESS",
            "imageUrl": "https://xxxx"
         }
    }
    ```
- code=22: Submission successful, waiting in queue
    ```json
    {
        "code": 22,
        "description": "In queue, there is 1 task ahead",
        "result": "0741798445574458",
        "properties": {
            "numberOfQueues": 1,
            "discordInstanceId": "1118138338562560102"
         }
    }
    ```
- code=23: Queue is full, please try again later
    ```json
    {
        "code": 23,
        "description": "Queue is full, please try again later",
        "result": "14001929738841620",
        "properties": {
            "discordInstanceId": "1118138338562560102"
         }
    }
    ```
- code=24: Prompt contains sensitive words
    ```json
    {
        "code": 24,
        "description": "May contain sensitive words",
        "properties": {
            "promptEn": "nude body",
            "bannedWord": "nude"
         }
    }
    ```
- other: Submission error, description is the error description

## 3. `/mj/submit/simple-change` Simple Drawing Change
This interface functions the same as `/mj/submit/change` (drawing change), but the parameter passing method is different. This interface receives content in the format of `ID operation`, for example: 1320098173412546 U2

- Zoom U1～U4
- Transform V1～V4
- Re-execute R

## 4. `/mj/submit/describe` Image to Text
```json
{
    // Base64 string of the image
    "base64": "data:image/png;base64,xxx"
}
```

After the subsequent tasks are completed, the finalPrompt in properties will be the prompt used for image generation.
```json
{
  "id":"14001929738841620",
  "action":"DESCRIBE",
  "status": "SUCCESS",
  "description":"/describe 14001929738841620.png",
  "imageUrl":"https://cdn.discordapp.com/attachments/xxx/xxx/14001929738841620.png",
  "properties": {
    "finalPrompt": "1️⃣ Cat --ar 5:4\n\n2️⃣ Cat2 --ar 5:4\n\n3️⃣ Cat3 --ar 5:4\n\n4️⃣ Cat4 --ar 5:4"
  }
  // ...
}
```

## 5. Task Change Callback
When the task status changes or progress updates, the business system's interface will be called.
- The interface address is the configured mj.notify-hook, and the task submission supports passing `notifyHook` to change the callback address for this task.
- If both are empty, no callback will be triggered.

POST  application/json
```json
{
  "id": "14001929738841620",
  "action": "IMAGINE",
  "status": "SUCCESS",
  "prompt": "猫猫",
  "promptEn": "Cat",
  "description": "/imagine 猫猫",
  "submitTime": 1689231405854,
  "startTime": 1689231442755,
  "finishTime": 1689231544312,
  "progress": "100%",
  "imageUrl": "https://cdn.discordapp.com/attachments/xxx/xxx/xxxx.png",
  "failReason": null,
  "properties": {
    "finalPrompt": "Cat"
  }
}
```
