import { Injectable, signal } from '@angular/core';
import { Toast } from '../Shared/models/toast.model';

@Injectable({
  providedIn:'root'
})
export class ToastService {

  toasts = signal<Toast[]>([]);

  private id = 0;

  show(
    title:string,
    message:string,
    type:'success'|'error'|'warning'|'info'='success',
    duration:number=4000
  ){

    const toast:Toast={

      id:++this.id,

      title,

      message,

      type

    };

    this.toasts.update(list=>[...list,toast]);

    setTimeout(()=>{

      this.remove(toast.id);

    },duration);

  }

  remove(id:number){

    this.toasts.update(list=>

      list.filter(x=>x.id!==id)

    );

  }

}