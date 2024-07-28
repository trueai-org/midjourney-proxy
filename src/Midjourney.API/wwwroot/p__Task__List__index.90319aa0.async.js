"use strict";(self.webpackChunkmidjourney_proxy_admin=self.webpackChunkmidjourney_proxy_admin||[]).push([[502],{86699:function(Te,A,n){n.r(A),n.d(A,{default:function(){return oe}});var H=n(97857),N=n.n(H),V=n(15009),h=n.n(V),W=n(99289),b=n.n(W),w=n(5574),v=n.n(w),z=n(28248),e=n(85893),J=function(p){var a=p.title,s=p.modalVisible,T=p.hideModal,C=p.modalContent,Z=p.footer,g=p.modalWidth;return(0,e.jsx)(z.Z,{title:a,open:s,onCancel:T,footer:Z,width:g,children:C})},K=J,O=n(80854),S=n(66309),L=n(38703),Q=n(83062),Y=n(11499),X=n(74330),k=n(4393),i=n(26412),q=function(p){var a=p.record,s=(0,O.useIntl)(),T=sessionStorage.getItem("mj-image-prefix")||"",C=function(t){var m="default";return t=="NOT_START"?m="default":t=="SUBMITTED"?m="lime":t=="MODAL"?m="warning":t=="IN_PROGRESS"?m="processing":t=="FAILURE"?m="error":t=="SUCCESS"?m="success":t=="CANCEL"&&(m="magenta"),(0,e.jsx)(S.Z,{color:m,children:a.displays.status})},Z=function(t){var m=0;t&&t.indexOf("%")>0&&(m=parseInt(t.substring(0,t.indexOf("%"))));var x="normal";return a.status=="SUCCESS"?x="success":a.status=="FAILURE"&&(x="exception"),(0,e.jsx)("div",{style:{width:200},children:(0,e.jsx)(L.Z,{percent:m,status:x})})},g=function(t){return!t||t.length<30?t:(0,e.jsx)(Q.Z,{title:t,children:t.substring(0,30)+"..."})},y=function(t){return t&&(0,e.jsx)(Y.Z,{width:200,src:T+t,placeholder:(0,e.jsx)(X.Z,{tip:"Loading",size:"large"})})},R=function(t){return t=="NIJI_JOURNEY"?(0,e.jsx)(S.Z,{color:"green",children:"niji\u30FBjourney"}):t=="INSIGHT_FACE"?(0,e.jsx)(S.Z,{color:"volcano",children:"InsightFace"}):(0,e.jsx)(S.Z,{color:"blue",children:"Midjourney"})},E=function(t){if(!(t==null||!t))return(0,e.jsx)(S.Z,{color:"green",children:s.formatMessage({id:"pages.yes"})})};return(0,e.jsxs)(e.Fragment,{children:[(0,e.jsx)(k.Z,{type:"inner",title:s.formatMessage({id:"pages.account.basicInfo"}),style:{margin:"10px"},children:(0,e.jsxs)(i.Z,{column:2,children:[(0,e.jsx)(i.Z.Item,{label:"ID",children:a.id}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.type"}),children:a.displays.action}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.status"}),children:C(a.status)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.progress"}),children:Z(a.progress)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.prompt"}),children:g(a.prompt)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.promptEn"}),children:g(a.promptEn)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.description"}),children:g(a.description)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.submitTime"}),children:a.displays.submitTime}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.startTime"}),children:a.displays.startTime}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.finishTime"}),children:a.displays.finishTime}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.failReason"}),children:g(a.failReason)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.state"}),children:g(a.state)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.image"}),children:y(a.imageUrl)})]})}),(0,e.jsx)(k.Z,{type:"inner",title:s.formatMessage({id:"pages.task.extendedInfo"}),style:{margin:"10px"},children:(0,e.jsxs)(i.Z,{column:2,children:[(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.botType"}),children:R(a.botType)}),(0,e.jsx)(i.Z.Item,{label:"Nonce",children:a.nonce}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.account.channelId"}),children:a.properties.discordInstanceId}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.instanceId"}),children:a.properties.discordInstanceId}),(0,e.jsx)(i.Z.Item,{label:"Hash",children:a.properties.messageHash}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.messageContent"}),children:g(a.properties.messageContent)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.finalPrompt"}),children:g(a.properties.finalPrompt)}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.finalPromptZh"}),children:g(a.promptCn||"-")}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.actionId"}),children:g(a.properties.custom_id||"-")}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.modalConfirm"}),children:E(a.properties.needModel)||"-"}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.imageSeed"}),children:a.seed||"-"}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.notifyHook"}),children:g(a.properties.notifyHook||"-")}),(0,e.jsx)(i.Z.Item,{label:s.formatMessage({id:"pages.task.ip"}),children:a.clientIp||"-"})]})})]})},_=q,U=n(66927),ee=n(82061),ae=n(90930),se=n(98695),te=n(53025),re=n(16568),ne=n(86738),ie=n(14726),j=n(67294),le=function(){var p=(0,j.useState)(!1),a=v()(p,2),s=a[0],T=a[1],C=(0,j.useState)({}),Z=v()(C,2),g=Z[0],y=Z[1],R=(0,j.useState)(""),E=v()(R,2),I=E[0],t=E[1],m=(0,j.useState)({}),x=v()(m,2),de=x[0],P=x[1],ue=(0,j.useState)(1e3),D=v()(ue,2),ce=D[0],ge=D[1],me=te.Z.useForm(),fe=v()(me,1),pe=fe[0],u=(0,O.useIntl)(),he=re.ZP.useNotification(),B=v()(he,2),$=B[0],ve=B[1],G=(0,j.useRef)(),Ie=function(){y({}),P({}),T(!1)},Me=function(r,l,o,d){pe.resetFields(),t(r),y(l),P(o),ge(d),T(!0)},Se=function(){var c=b()(h()().mark(function r(l){var o,d;return h()().wrap(function(f){for(;;)switch(f.prev=f.next){case 0:return f.prev=0,f.next=3,(0,U._5)(l);case 3:o=f.sent,o.success?($.success({message:"success",description:u.formatMessage({id:"pages.task.deleteSuccess"})}),(d=G.current)===null||d===void 0||d.reload()):$.error({message:"error",description:o.message}),f.next=10;break;case 7:f.prev=7,f.t0=f.catch(0),console.error(f.t0);case 10:return f.prev=10,f.finish(10);case 12:case"end":return f.stop()}},r,null,[[0,7,10,12]])}));return function(l){return c.apply(this,arguments)}}(),je=[{title:"ID",dataIndex:"id",width:160,align:"center",fixed:"left",render:function(r,l){return(0,e.jsx)("a",{onClick:function(){return Me(u.formatMessage({id:"pages.task.info"}),(0,e.jsx)(_,{record:l}),null,1100)},children:r})}},{title:u.formatMessage({id:"pages.task.type"}),dataIndex:"action",width:100,align:"center",request:function(){var c=b()(h()().mark(function l(){return h()().wrap(function(d){for(;;)switch(d.prev=d.next){case 0:return d.abrupt("return",[{label:"Imagine",value:"IMAGINE"},{label:"Upscale",value:"UPSCALE"},{label:"Variation",value:"VARIATION"},{label:"Zoom",value:"ZOOM"},{label:"Pan",value:"PAN"},{label:"Describe",value:"DESCRIBE"},{label:"Blend",value:"BLEND"},{label:"Shorten",value:"SHORTEN"},{label:"SwapFace",value:"SWAP_FACE"}]);case 1:case"end":return d.stop()}},l)}));function r(){return c.apply(this,arguments)}return r}(),render:function(r,l){return l.displays.action}},{title:u.formatMessage({id:"pages.task.instanceId"}),dataIndex:"instanceId",width:180,align:"center",render:function(r,l){return l.properties.discordInstanceId}},{title:u.formatMessage({id:"pages.task.submitTime"}),dataIndex:"submitTime",width:160,hideInSearch:!0,align:"center",render:function(r,l){return l.displays.submitTime}},{title:u.formatMessage({id:"pages.task.status"}),dataIndex:"status",width:90,align:"center",request:function(){var c=b()(h()().mark(function l(){return h()().wrap(function(d){for(;;)switch(d.prev=d.next){case 0:return d.abrupt("return",[{label:u.formatMessage({id:"pages.task.NOT_START"}),value:"NOT_START"},{label:u.formatMessage({id:"pages.task.SUBMITTED"}),value:"SUBMITTED"},{label:u.formatMessage({id:"pages.task.MODAL"}),value:"MODAL"},{label:u.formatMessage({id:"pages.task.IN_PROGRESS"}),value:"IN_PROGRESS"},{label:u.formatMessage({id:"pages.task.FAILURE"}),value:"FAILURE"},{label:u.formatMessage({id:"pages.task.SUCCESS"}),value:"SUCCESS"},{label:u.formatMessage({id:"pages.task.CANCEL"}),value:"CANCEL"}]);case 1:case"end":return d.stop()}},l)}));function r(){return c.apply(this,arguments)}return r}(),render:function(r,l){var o="default";return r=="NOT_START"?o="default":r=="SUBMITTED"?o="lime":r=="MODAL"?o="warning":r=="IN_PROGRESS"?o="processing":r=="FAILURE"?o="error":r=="SUCCESS"?o="success":r=="CANCEL"&&(o="magenta"),(0,e.jsx)(S.Z,{color:o,children:l.displays.status})}},{title:u.formatMessage({id:"pages.task.progress"}),dataIndex:"progress",width:130,showInfo:!1,hideInSearch:!0,render:function(r,l){var o=0;r&&r.indexOf("%")>0&&(o=parseInt(r.substring(0,r.indexOf("%"))));var d="normal";return l.status=="SUCCESS"?d="success":l.status=="FAILURE"&&(d="exception"),(0,e.jsx)(L.Z,{percent:o,status:d,size:"small"})}},{title:u.formatMessage({id:"pages.task.description"}),dataIndex:"description",width:250,ellipsis:!0},{title:u.formatMessage({id:"pages.task.failReason"}),dataIndex:"failReason",width:220,ellipsis:!0},{title:u.formatMessage({id:"pages.operation"}),dataIndex:"operation",width:100,key:"operation",fixed:"right",align:"center",hideInSearch:!0,render:function(r,l){return(0,e.jsx)(ne.Z,{title:u.formatMessage({id:"pages.task.delete"}),description:u.formatMessage({id:"pages.task.deleteTitle"}),onConfirm:function(){return Se(l.id)},children:(0,e.jsx)(ie.ZP,{danger:!0,icon:(0,e.jsx)(ee.Z,{})})})}}];return(0,e.jsxs)(ae._z,{children:[ve,(0,e.jsx)(k.Z,{children:(0,e.jsx)(se.Z,{columns:je,scroll:{x:1e3},search:{defaultCollapsed:!0},pagination:{pageSize:10,showQuickJumper:!1,showSizeChanger:!1},rowKey:"id",actionRef:G,request:function(){var c=b()(h()().mark(function r(l){var o;return h()().wrap(function(M){for(;;)switch(M.prev=M.next){case 0:return M.next=2,(0,U.Pn)(N()(N()({},l),{},{pageNumber:l.current-1}));case 2:return o=M.sent,M.abrupt("return",{data:o.list,total:o.pagination.total,success:!0});case 4:case"end":return M.stop()}},r)}));return function(r){return c.apply(this,arguments)}}()})}),(0,e.jsx)(K,{title:I,modalVisible:s,hideModal:Ie,modalContent:g,footer:de,modalWidth:ce})]})},oe=le}}]);