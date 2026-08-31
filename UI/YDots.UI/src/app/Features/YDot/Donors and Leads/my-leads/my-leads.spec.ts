import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MyLeads } from './my-leads';

describe('MyLeads', () => {
  let component: MyLeads;
  let fixture: ComponentFixture<MyLeads>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyLeads],
    }).compileComponents();

    fixture = TestBed.createComponent(MyLeads);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
