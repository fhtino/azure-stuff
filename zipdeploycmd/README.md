# ZipdDeployCmd

ZipdDeployCmd is a small utility designed to ease Azure App Service zip deployment from command line. 
It uses PublishSettings file to get required information.
Credentials are stored using Windows Data Protection API (DPAPI).
Publishing runs using protected data. No plain-text file required.
   
Underlying API:
 - https://github.com/projectkudu/kudu/wiki/REST-API
 - https://docs.microsoft.com/en-us/azure/app-service/deploy-zip
 - https://github.com/projectkudu/kudu/wiki/Deploying-from-a-zip-file-or-url


## Usage
Steps:
 - import PuglishSettings profile
 - publish zip to App Service

Exit codes:  
&nbsp;&nbsp; 0 : OK  
&nbsp;&nbsp; 1 : Error  

### Import
```
zipdeploycmd.exe IMPORT mysite.PublishSettings
```   
This creates a new file: mysite.pubconfig.json It contains required url and credentials (protected with DPAPI).
```json
{
  "Name": "mysite",
  "ScmHostName": "mysite.scm.azurewebsites.net",
  "UserName": "$mysite",
  "PasswordEnc": "AQAAANC....==",
  "DT": "2021-05-23T10:00:00Z"
}
```

### Publish

```
zipdeploycmd.exe PUBLISH mysite.pubconfig.json wwwroot.zip 
```   



